using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class PaymentIntegrationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _client;

        public PaymentIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private async Task<TestSession> RegisterOrganizerAsync()
        {
            return await AuthHelper.RegisterOrganizerAsync(_client);
        }

        private async Task<TestSession> RegisterAttendeeAsync()
        {
            return await AuthHelper.RegisterAttendeeAsync(_client);
        }

        private async Task<Event> CreateEventAsync(
            TestSession organizer,
            string title,
            TicketTypeSpec[]? tiers = null)
        {
            return await TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                EventStatus.Published,
                startsAt: DateTime.UtcNow.AddDays(5),
                imagePath: "events/test/cover.jpg",
                ticketTypes: tiers ?? new[]
                {
                    new TicketTypeSpec(
                        "General",
                        100m,
                        2,
                        SalesOpenAt: DateTime.UtcNow.AddHours(-1))
                },
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private static TicketType Tier(Event eventEntity, int index = 0)
        {
            return eventEntity.TicketTypes.OrderBy(item => item.Id).ElementAt(index);
        }

        private static TicketType TierByName(Event eventEntity, string name)
        {
            return eventEntity.TicketTypes.Single(item => item.Name == name);
        }

        private async Task FundAsync(TestSession attendee, decimal amount)
        {
            var response = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task<ReservationResponse> ReserveSuccessfullyAsync(
            TestSession attendee,
            Event eventEntity,
            TicketType tier)
        {
            var response = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/reservations",
                new { ticketTypeId = tier.Id },
                attendee.AccessToken);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Reservation failed with {(int)response.StatusCode}. Body: {body}");
            }

            return (await response.Content.ReadFromJsonAsync<ReservationResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private async Task<HttpResponseMessage> SendWithIdempotencyKeyAsync(
            HttpMethod method,
            string path,
            string token,
            string? idempotencyKey,
            object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());
            if (idempotencyKey is not null)
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        private Task<HttpResponseMessage> PayAsync(
            Guid reservationId,
            string token,
            string? idempotencyKey)
        {
            return SendWithIdempotencyKeyAsync(
                HttpMethod.Post,
                $"/api/v1/reservations/{reservationId}/payment",
                token,
                idempotencyKey);
        }

        private Task<HttpResponseMessage> BuyNowAsync(
            Guid eventId,
            Guid ticketTypeId,
            string token,
            string? idempotencyKey)
        {
            return SendWithIdempotencyKeyAsync(
                HttpMethod.Post,
                $"/api/v1/events/{eventId}/purchase",
                token,
                idempotencyKey,
                new { ticketTypeId });
        }

        private static async Task<PaymentResponse> ReadPaymentAsync(HttpResponseMessage response)
        {
            return (await response.Content.ReadFromJsonAsync<PaymentResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private static async Task UpdateReservationAsync(
            Guid reservationId,
            Action<Reservation> update)
        {
            await using var db = TestDatabase.CreateDbContext();
            var reservation = await db.Reservations.SingleAsync(
                item => item.Id == reservationId,
                TestContext.Current.CancellationToken);
            update(reservation);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private static async Task<decimal> GetBalanceAsync(Guid userId)
        {
            await using var db = TestDatabase.CreateDbContext();
            return await db.Wallets
                .Where(wallet => wallet.UserId == userId)
                .Select(wallet => wallet.Balance)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        private static void AssertTicketCode(string code)
        {
            Assert.Matches(new Regex("^[A-HJ-NP-Z2-9]{8}$"), code);
        }

        [Fact]
        public async Task Pay_IssuesTicketDebitsWalletAndReplaysTheSameKey()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var stranger = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(organizer, TestRun.NewTitle("pay-success"));
            var tier = Tier(eventEntity);

            await FundAsync(attendee, 500m);
            var reservation = await ReserveSuccessfullyAsync(attendee, eventEntity, tier);

            var missingKeyResponse = await PayAsync(reservation.Id, attendee.AccessToken, null);
            Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);

            var payResponse = await PayAsync(
                reservation.Id,
                attendee.AccessToken,
                "pay-success-key");
            Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);
            var payment = await ReadPaymentAsync(payResponse);
            Assert.Equal(reservation.Id, payment.ReservationId);
            Assert.Equal(eventEntity.Id, payment.EventId);
            Assert.Equal(ReservationStatus.Paid, payment.Status);
            Assert.Equal(100m, payment.PricePaid);
            Assert.NotEqual(Guid.Empty, payment.TicketId);
            AssertTicketCode(payment.Code);

            Assert.Equal(400m, await GetBalanceAsync(Guid.Parse(attendee.UserId)));

            await using (var db = TestDatabase.CreateDbContext())
            {
                var persisted = await db.Reservations.SingleAsync(
                    item => item.Id == reservation.Id,
                    TestContext.Current.CancellationToken);
                Assert.Equal(ReservationStatus.Paid, persisted.Status);
                Assert.Equal("pay-success-key", persisted.PaymentIdempotencyKey);
                Assert.NotNull(persisted.PaidAt);

                var ticket = await db.Tickets.SingleAsync(
                    item => item.ReservationId == reservation.Id,
                    TestContext.Current.CancellationToken);
                Assert.Equal(payment.TicketId, ticket.Id);
                Assert.Equal(payment.Code, ticket.Code);

                var purchases = await db.WalletTransactions
                    .Where(item => item.UserId == persisted.UserId
                        && item.Type == WalletTransactionType.Purchase)
                    .ToListAsync(TestContext.Current.CancellationToken);
                var purchase = Assert.Single(purchases);
                Assert.Equal(-100m, purchase.Amount);
                Assert.Equal(payment.Code, purchase.Reference);
            }

            var replayResponse = await PayAsync(
                reservation.Id,
                attendee.AccessToken,
                "pay-success-key");
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            var replay = await ReadPaymentAsync(replayResponse);
            Assert.Equal(payment.TicketId, replay.TicketId);
            Assert.Equal(payment.Code, replay.Code);
            Assert.Equal(reservation.Id, replay.ReservationId);
            Assert.Equal(400m, await GetBalanceAsync(Guid.Parse(attendee.UserId)));

            var differentKeyResponse = await PayAsync(
                reservation.Id,
                attendee.AccessToken,
                "pay-different-key");
            Assert.Equal(HttpStatusCode.Conflict, differentKeyResponse.StatusCode);

            var tooLongKeyResponse = await PayAsync(
                reservation.Id,
                attendee.AccessToken,
                new string('k', 101));
            Assert.Equal(HttpStatusCode.BadRequest, tooLongKeyResponse.StatusCode);

            var foreignResponse = await PayAsync(
                reservation.Id,
                stranger.AccessToken,
                "pay-foreign-key");
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

            var missingReservationResponse = await PayAsync(
                Guid.NewGuid(),
                stranger.AccessToken,
                "pay-missing-key");
            Assert.Equal(HttpStatusCode.NotFound, missingReservationResponse.StatusCode);

            await using var verifyDb = TestDatabase.CreateDbContext();
            Assert.Equal(1, await verifyDb.Tickets.CountAsync(
                item => item.ReservationId == reservation.Id,
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await verifyDb.WalletTransactions.CountAsync(
                item => item.UserId == Guid.Parse(attendee.UserId)
                    && item.Type == WalletTransactionType.Purchase,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Pay_InsufficientFundsExpiredAndCancelledHaveDistinctOutcomes()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();

            var fundingEvent = await CreateEventAsync(organizer, TestRun.NewTitle("pay-funding"));
            var fundingReservation = await ReserveSuccessfullyAsync(
                attendee,
                fundingEvent,
                Tier(fundingEvent));

            var insufficientResponse = await PayAsync(
                fundingReservation.Id,
                attendee.AccessToken,
                "pay-retry-key");
            Assert.Equal(HttpStatusCode.Conflict, insufficientResponse.StatusCode);

            await using (var db = TestDatabase.CreateDbContext())
            {
                var reservation = await db.Reservations.SingleAsync(
                    item => item.Id == fundingReservation.Id,
                    TestContext.Current.CancellationToken);
                Assert.Equal(ReservationStatus.Held, reservation.Status);
                Assert.Null(reservation.PaymentIdempotencyKey);
                Assert.Equal(0, await db.Tickets.CountAsync(
                    item => item.ReservationId == fundingReservation.Id,
                    TestContext.Current.CancellationToken));
            }

            await FundAsync(attendee, 100m);
            var retryResponse = await PayAsync(
                fundingReservation.Id,
                attendee.AccessToken,
                "pay-retry-key");
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            var retryPayment = await ReadPaymentAsync(retryResponse);
            Assert.Equal(0m, await GetBalanceAsync(Guid.Parse(attendee.UserId)));

            await UpdateReservationAsync(fundingReservation.Id, item =>
            {
                item.PaidAt = DateTime.UtcNow.AddHours(-25);
            });

            var expiredKeyResponse = await PayAsync(
                fundingReservation.Id,
                attendee.AccessToken,
                "pay-retry-key");
            Assert.Equal(HttpStatusCode.Conflict, expiredKeyResponse.StatusCode);
            var expiredKeyError = await expiredKeyResponse.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal(
                "idempotency_key_expired",
                expiredKeyError.GetProperty("code").GetString());

            var cancelledEvent = await CreateEventAsync(organizer, TestRun.NewTitle("pay-cancelled"));
            var cancelledReservation = await ReserveSuccessfullyAsync(
                attendee,
                cancelledEvent,
                Tier(cancelledEvent));
            var cancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{cancelledReservation.Id}",
                token: attendee.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

            var cancelledPayResponse = await PayAsync(
                cancelledReservation.Id,
                attendee.AccessToken,
                "pay-cancelled-key");
            Assert.Equal(HttpStatusCode.Conflict, cancelledPayResponse.StatusCode);

            var expiredEvent = await CreateEventAsync(organizer, TestRun.NewTitle("pay-expired"));
            var expiredReservation = await ReserveSuccessfullyAsync(
                attendee,
                expiredEvent,
                Tier(expiredEvent));
            await UpdateReservationAsync(expiredReservation.Id, item =>
            {
                item.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
                item.ExpiresAt = DateTime.UtcNow.AddMinutes(-10);
                item.Status = ReservationStatus.Held;
            });

            var expiredPayResponse = await PayAsync(
                expiredReservation.Id,
                attendee.AccessToken,
                "pay-expired-key");
            Assert.Equal(HttpStatusCode.Gone, expiredPayResponse.StatusCode);
            var expiredError = await expiredPayResponse.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal("reservation_expired", expiredError.GetProperty("code").GetString());

            await using var verifyDb = TestDatabase.CreateDbContext();
            var stillHeld = await verifyDb.Reservations.SingleAsync(
                item => item.Id == expiredReservation.Id,
                TestContext.Current.CancellationToken);
            Assert.Equal(ReservationStatus.Held, stillHeld.Status);
            Assert.Equal(0, await verifyDb.Tickets.CountAsync(
                item => item.ReservationId == expiredReservation.Id,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BuyNow_CreatesPaidReservationAndReplaysTheSameKey()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var tiers = new[]
            {
                new TicketTypeSpec("Tier A", 100m, 2, SalesOpenAt: DateTime.UtcNow.AddHours(-1)),
                new TicketTypeSpec("Tier B", 150m, 2, SalesOpenAt: DateTime.UtcNow.AddHours(-1))
            };
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("buy-now"),
                tiers);
            var otherEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("buy-now-other"));
            var firstTier = TierByName(eventEntity, "Tier A");
            var secondTier = TierByName(eventEntity, "Tier B");

            await FundAsync(attendee, 500m);

            var purchaseResponse = await BuyNowAsync(
                eventEntity.Id,
                firstTier.Id,
                attendee.AccessToken,
                "buy-now-key");
            Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);
            var payment = await ReadPaymentAsync(purchaseResponse);
            Assert.Equal(eventEntity.Id, payment.EventId);
            Assert.Equal(100m, payment.PricePaid);
            Assert.Equal(ReservationStatus.Paid, payment.Status);
            AssertTicketCode(payment.Code);
            Assert.Equal(400m, await GetBalanceAsync(Guid.Parse(attendee.UserId)));

            await using (var db = TestDatabase.CreateDbContext())
            {
                var reservation = await db.Reservations.SingleAsync(
                    item => item.Id == payment.ReservationId,
                    TestContext.Current.CancellationToken);
                Assert.Equal(ReservationStatus.Paid, reservation.Status);
                Assert.Equal("buy-now-key", reservation.PaymentIdempotencyKey);
                Assert.Equal(firstTier.Id, reservation.TicketTypeId);
                Assert.NotNull(reservation.PaidAt);
                Assert.Equal(1, await db.Tickets.CountAsync(
                    item => item.EventId == eventEntity.Id,
                    TestContext.Current.CancellationToken));
            }

            var replayResponse = await BuyNowAsync(
                eventEntity.Id,
                firstTier.Id,
                attendee.AccessToken,
                "buy-now-key");
            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
            var replay = await ReadPaymentAsync(replayResponse);
            Assert.Equal(payment.ReservationId, replay.ReservationId);
            Assert.Equal(payment.TicketId, replay.TicketId);
            Assert.Equal(payment.Code, replay.Code);
            Assert.Equal(400m, await GetBalanceAsync(Guid.Parse(attendee.UserId)));

            var differentTierResponse = await BuyNowAsync(
                eventEntity.Id,
                secondTier.Id,
                attendee.AccessToken,
                "buy-now-key");
            Assert.Equal(HttpStatusCode.Conflict, differentTierResponse.StatusCode);

            var differentEventResponse = await BuyNowAsync(
                otherEvent.Id,
                Tier(otherEvent).Id,
                attendee.AccessToken,
                "buy-now-key");
            Assert.Equal(HttpStatusCode.Conflict, differentEventResponse.StatusCode);

            await UpdateReservationAsync(payment.ReservationId, item =>
            {
                item.PaidAt = DateTime.UtcNow.AddHours(-25);
            });

            var expiredKeyResponse = await BuyNowAsync(
                eventEntity.Id,
                firstTier.Id,
                attendee.AccessToken,
                "buy-now-key");
            Assert.Equal(HttpStatusCode.Conflict, expiredKeyResponse.StatusCode);
            var expiredKeyError = await expiredKeyResponse.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal(
                "idempotency_key_expired",
                expiredKeyError.GetProperty("code").GetString());
        }

        [Fact]
        public async Task BuyNow_ReusesHoldsAndHandlesExpiryStockAndFundingRetry()
        {
            var organizer = await RegisterOrganizerAsync();
            var heldBuyer = await RegisterAttendeeAsync();
            var mismatchBuyer = await RegisterAttendeeAsync();
            var expiredBuyer = await RegisterAttendeeAsync();
            var stockBuyer = await RegisterAttendeeAsync();
            var fundingBuyer = await RegisterAttendeeAsync();

            var heldEvent = await CreateEventAsync(organizer, TestRun.NewTitle("buy-held"));
            var heldTier = Tier(heldEvent);
            await FundAsync(heldBuyer, 200m);
            var heldReservation = await ReserveSuccessfullyAsync(heldBuyer, heldEvent, heldTier);

            var heldBuyResponse = await BuyNowAsync(
                heldEvent.Id,
                heldTier.Id,
                heldBuyer.AccessToken,
                "buy-held-key");
            Assert.Equal(HttpStatusCode.Created, heldBuyResponse.StatusCode);
            var heldPayment = await ReadPaymentAsync(heldBuyResponse);
            Assert.Equal(heldReservation.Id, heldPayment.ReservationId);
            Assert.Equal(ReservationStatus.Paid, heldPayment.Status);
            Assert.Equal(100m, await GetBalanceAsync(Guid.Parse(heldBuyer.UserId)));

            await using (var db = TestDatabase.CreateDbContext())
            {
                Assert.Equal(1, await db.Reservations.CountAsync(
                    item => item.EventId == heldEvent.Id
                        && item.UserId == Guid.Parse(heldBuyer.UserId),
                    TestContext.Current.CancellationToken));
            }

            var mismatchEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("buy-mismatch"),
                new[]
                {
                    new TicketTypeSpec("Tier A", 100m, 2, SalesOpenAt: DateTime.UtcNow.AddHours(-1)),
                    new TicketTypeSpec("Tier B", 150m, 2, SalesOpenAt: DateTime.UtcNow.AddHours(-1))
                });
            var mismatchTierA = TierByName(mismatchEvent, "Tier A");
            var mismatchTierB = TierByName(mismatchEvent, "Tier B");
            await FundAsync(mismatchBuyer, 500m);
            var mismatchHold = await ReserveSuccessfullyAsync(
                mismatchBuyer,
                mismatchEvent,
                mismatchTierA);

            var holdMismatchResponse = await BuyNowAsync(
                mismatchEvent.Id,
                mismatchTierB.Id,
                mismatchBuyer.AccessToken,
                "buy-mismatch-key");
            Assert.Equal(HttpStatusCode.Conflict, holdMismatchResponse.StatusCode);

            var cancelHoldResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{mismatchHold.Id}",
                token: mismatchBuyer.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, cancelHoldResponse.StatusCode);

            var cancelledBuyResponse = await BuyNowAsync(
                mismatchEvent.Id,
                mismatchTierB.Id,
                mismatchBuyer.AccessToken,
                "buy-cancelled-key");
            Assert.Equal(HttpStatusCode.Conflict, cancelledBuyResponse.StatusCode);
            Assert.Equal(500m, await GetBalanceAsync(Guid.Parse(mismatchBuyer.UserId)));

            var stockEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("buy-stock"),
                new[]
                {
                    new TicketTypeSpec("Last", 100m, 1, SalesOpenAt: DateTime.UtcNow.AddHours(-1))
                });
            var stockTier = Tier(stockEvent);
            await FundAsync(stockBuyer, 100m);
            await FundAsync(expiredBuyer, 100m);
            var expiredHold = await ReserveSuccessfullyAsync(
                expiredBuyer,
                stockEvent,
                stockTier);
            await UpdateReservationAsync(expiredHold.Id, item =>
            {
                item.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
                item.ExpiresAt = DateTime.UtcNow.AddMinutes(-10);
                item.Status = ReservationStatus.Held;
            });

            var stockBuyResponse = await BuyNowAsync(
                stockEvent.Id,
                stockTier.Id,
                stockBuyer.AccessToken,
                "buy-stock-key");
            Assert.Equal(HttpStatusCode.Created, stockBuyResponse.StatusCode);
            var stockPayment = await ReadPaymentAsync(stockBuyResponse);
            Assert.Equal(ReservationStatus.Paid, stockPayment.Status);
            AssertTicketCode(stockPayment.Code);

            var soldOutResponse = await BuyNowAsync(
                stockEvent.Id,
                stockTier.Id,
                expiredBuyer.AccessToken,
                "buy-expired-key");
            Assert.Equal(HttpStatusCode.Conflict, soldOutResponse.StatusCode);
            Assert.Equal(100m, await GetBalanceAsync(Guid.Parse(expiredBuyer.UserId)));

            await using (var db = TestDatabase.CreateDbContext())
            {
                Assert.Equal(1, await db.Tickets.CountAsync(
                    item => item.EventId == stockEvent.Id,
                    TestContext.Current.CancellationToken));
                Assert.Equal(2, await db.Reservations.CountAsync(
                    item => item.EventId == stockEvent.Id,
                    TestContext.Current.CancellationToken));
            }

            var fundingEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("buy-funding"));
            var fundingTier = Tier(fundingEvent);

            var noFundsResponse = await BuyNowAsync(
                fundingEvent.Id,
                fundingTier.Id,
                fundingBuyer.AccessToken,
                "buy-funding-key");
            Assert.Equal(HttpStatusCode.Conflict, noFundsResponse.StatusCode);

            await FundAsync(fundingBuyer, 100m);
            var fundedRetryResponse = await BuyNowAsync(
                fundingEvent.Id,
                fundingTier.Id,
                fundingBuyer.AccessToken,
                "buy-funding-key");
            Assert.Equal(HttpStatusCode.Created, fundedRetryResponse.StatusCode);
            var fundedPayment = await ReadPaymentAsync(fundedRetryResponse);
            Assert.Equal(ReservationStatus.Paid, fundedPayment.Status);
            Assert.Equal(0m, await GetBalanceAsync(Guid.Parse(fundingBuyer.UserId)));
        }

        [Fact]
        public async Task PaymentEndpoints_RequireAttendeeAndRateLimitBuyNow()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var rateAttendee = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("payment-permissions"));
            var tier = Tier(eventEntity);

            var anonymousPayResponse = await PayAsync(
                Guid.NewGuid(),
                string.Empty,
                "permission-key");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPayResponse.StatusCode);

            var organizerPayResponse = await PayAsync(
                Guid.NewGuid(),
                organizer.AccessToken,
                "permission-key");
            Assert.Equal(HttpStatusCode.Forbidden, organizerPayResponse.StatusCode);

            var anonymousBuyResponse = await BuyNowAsync(
                eventEntity.Id,
                tier.Id,
                string.Empty,
                "permission-key");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousBuyResponse.StatusCode);

            var organizerBuyResponse = await BuyNowAsync(
                eventEntity.Id,
                tier.Id,
                organizer.AccessToken,
                "permission-key");
            Assert.Equal(HttpStatusCode.Forbidden, organizerBuyResponse.StatusCode);

            var missingKeyResponse = await SendWithIdempotencyKeyAsync(
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/purchase",
                attendee.AccessToken,
                null,
                new { ticketTypeId = tier.Id });
            Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);

            var emptyKeyResponse = await SendWithIdempotencyKeyAsync(
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/purchase",
                attendee.AccessToken,
                " ",
                new { ticketTypeId = tier.Id });
            Assert.Equal(HttpStatusCode.BadRequest, emptyKeyResponse.StatusCode);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var rejectedResponse = await BuyNowAsync(
                    eventEntity.Id,
                    Guid.NewGuid(),
                    rateAttendee.AccessToken,
                    $"buy-rate-{attempt}");
                Assert.Equal(HttpStatusCode.NotFound, rejectedResponse.StatusCode);
            }

            var limitedResponse = await BuyNowAsync(
                eventEntity.Id,
                Guid.NewGuid(),
                rateAttendee.AccessToken,
                "buy-rate-limited");
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        }
    }
}
