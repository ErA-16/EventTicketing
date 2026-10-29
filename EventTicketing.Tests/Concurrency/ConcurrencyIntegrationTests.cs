using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Concurrency
{
    public sealed class ConcurrencyIntegrationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _client;

        public ConcurrencyIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private async Task<Event> CreateEventAsync(
            TestSession organizer,
            string title,
            decimal price,
            int quantity)
        {
            return await TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                EventStatus.Published,
                startsAt: DateTime.UtcNow.AddDays(3),
                ticketTypes: new[]
                {
                    new TicketTypeSpec(
                        "General",
                        price,
                        quantity,
                        SalesOpenAt: DateTime.UtcNow.AddHours(-1))
                },
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private static TicketType Tier(Event eventEntity)
        {
            return eventEntity.TicketTypes.Single();
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

        private async Task<HttpResponseMessage> BuyNowAsync(
            TestSession attendee,
            Event eventEntity,
            string idempotencyKey)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/purchase");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", attendee.AccessToken);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Content = JsonContent.Create(new { ticketTypeId = Tier(eventEntity).Id });
            return await _client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
        }

        private async Task<PaymentResponse> ReadPaymentAsync(
            HttpResponseMessage response)
        {
            return (await response.Content.ReadFromJsonAsync<PaymentResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        [Fact]
        public async Task LastTicket_UnderConcurrentBuyers_IsSoldExactlyOnce()
        {
            var organizer = await AuthHelper.RegisterOrganizerAsync(_client);
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("concurrent-last-ticket"),
                50m,
                1);
            var buyers = new List<TestSession>();
            for (var index = 0; index < 10; index++)
            {
                var buyer = await AuthHelper.RegisterAttendeeAsync(_client);
                await FundAsync(buyer, 50m);
                buyers.Add(buyer);
            }

            var responses = await Task.WhenAll(
                buyers.Select((buyer, index) => BuyNowAsync(
                    buyer,
                    eventEntity,
                    $"last-ticket-{index}")));

            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(9, responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

            await using var db = TestDatabase.CreateDbContext();
            var paidReservations = await db.Reservations
                .Where(item => item.EventId == eventEntity.Id
                    && item.Status == ReservationStatus.Paid)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(paidReservations);
            Assert.Equal(1, await db.Tickets.CountAsync(
                item => item.EventId == eventEntity.Id,
                TestContext.Current.CancellationToken));

            var winnerId = paidReservations[0].UserId;
            var winner = buyers.Single(buyer => Guid.Parse(buyer.UserId) == winnerId);
            Assert.Equal(0m, await GetBalanceAsync(winnerId));

            foreach (var loser in buyers.Where(
                buyer => buyer.UserId != winner.UserId))
            {
                Assert.Equal(50m, await GetBalanceAsync(Guid.Parse(loser.UserId)));
            }

            Assert.Equal(1, await db.WalletTransactions.CountAsync(
                item => item.UserId == winnerId
                    && item.Type == WalletTransactionType.Purchase,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SameIdempotencyKey_UnderConcurrentBuyers_ReplaysOneTicket()
        {
            var organizer = await AuthHelper.RegisterOrganizerAsync(_client);
            var buyer = await AuthHelper.RegisterAttendeeAsync(_client);
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("concurrent-same-key"),
                100m,
                2);
            await FundAsync(buyer, 500m);

            var responses = await Task.WhenAll(
                Enumerable.Range(0, 5)
                    .Select(_ => BuyNowAsync(buyer, eventEntity, "same-key-purchase")));

            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var payments = await Task.WhenAll(responses.Select(ReadPaymentAsync));
            var first = payments[0];
            Assert.All(
                payments,
                payment =>
                {
                    Assert.Equal(first.ReservationId, payment.ReservationId);
                    Assert.Equal(first.TicketId, payment.TicketId);
                    Assert.Equal(first.Code, payment.Code);
                });

            await using var db = TestDatabase.CreateDbContext();
            Assert.Equal(1, await db.Reservations.CountAsync(
                item => item.EventId == eventEntity.Id,
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await db.Tickets.CountAsync(
                item => item.EventId == eventEntity.Id,
                TestContext.Current.CancellationToken));
            Assert.Equal(400m, await GetBalanceAsync(Guid.Parse(buyer.UserId)));
            Assert.Equal(1, await db.WalletTransactions.CountAsync(
                item => item.UserId == Guid.Parse(buyer.UserId)
                    && item.Type == WalletTransactionType.Purchase,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SameUserConcurrentReserve_CreatesExactlyOneHold()
        {
            var organizer = await AuthHelper.RegisterOrganizerAsync(_client);
            var attendee = await AuthHelper.RegisterAttendeeAsync(_client);
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("concurrent-reserve"),
                100m,
                5);

            var first = AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/reservations",
                new { ticketTypeId = Tier(eventEntity).Id },
                attendee.AccessToken);
            var second = AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/reservations",
                new { ticketTypeId = Tier(eventEntity).Id },
                attendee.AccessToken);
            var responses = await Task.WhenAll(first, second);

            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

            await using var db = TestDatabase.CreateDbContext();
            Assert.Equal(1, await db.Reservations.CountAsync(
                item => item.EventId == eventEntity.Id,
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task TwoEvents_UnderOneWallet_ChargeAtMostOnce()
        {
            var organizer = await AuthHelper.RegisterOrganizerAsync(_client);
            var buyer = await AuthHelper.RegisterAttendeeAsync(_client);
            var firstEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("concurrent-wallet-first"),
                60m,
                1);
            var secondEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("concurrent-wallet-second"),
                60m,
                1);
            await FundAsync(buyer, 100m);

            var first = BuyNowAsync(buyer, firstEvent, "wallet-race-first");
            var second = BuyNowAsync(buyer, secondEvent, "wallet-race-second");
            var responses = await Task.WhenAll(first, second);

            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

            await using var db = TestDatabase.CreateDbContext();
            var wallet = await db.Wallets.SingleAsync(
                item => item.UserId == Guid.Parse(buyer.UserId),
                TestContext.Current.CancellationToken);
            Assert.Equal(40m, wallet.Balance);
            Assert.Equal(1, await db.Tickets.CountAsync(
                item => item.UserId == Guid.Parse(buyer.UserId),
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await db.WalletTransactions.CountAsync(
                item => item.UserId == Guid.Parse(buyer.UserId)
                    && item.Type == WalletTransactionType.Purchase,
                TestContext.Current.CancellationToken));
        }

        private static async Task<decimal> GetBalanceAsync(Guid userId)
        {
            await using var db = TestDatabase.CreateDbContext();
            return await db.Wallets
                .Where(item => item.UserId == userId)
                .Select(item => item.Balance)
                .SingleAsync(TestContext.Current.CancellationToken);
        }
    }
}
