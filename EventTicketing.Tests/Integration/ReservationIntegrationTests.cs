using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class ReservationIntegrationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _client;

        public ReservationIntegrationTests(ApiFixture fixture)
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
            EventStatus status = EventStatus.Published,
            DateTime? startsAt = null,
            TicketTypeSpec[]? tiers = null)
        {
            return await TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                status,
                startsAt: startsAt ?? DateTime.UtcNow.AddDays(5),
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

        private async Task<HttpResponseMessage> ReserveAsync(
            TestSession attendee,
            Guid eventId,
            Guid ticketTypeId)
        {
            return await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventId}/reservations",
                new { ticketTypeId },
                attendee.AccessToken);
        }

        private async Task<ReservationResponse> ReserveSuccessfullyAsync(
            TestSession attendee,
            Guid eventId,
            Guid ticketTypeId)
        {
            var response = await ReserveAsync(attendee, eventId, ticketTypeId);
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

        [Fact]
        public async Task Reserve_CreatesHoldAndEnforcesStockAndOnePerEvent()
        {
            var organizer = await RegisterOrganizerAsync();
            var first = await RegisterAttendeeAsync();
            var second = await RegisterAttendeeAsync();
            var third = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-stock"));
            var tier = Tier(eventEntity);

            var before = DateTime.UtcNow;
            var firstReservation = await ReserveSuccessfullyAsync(first, eventEntity.Id, tier.Id);

            Assert.Equal(ReservationStatus.Held, firstReservation.Status);
            Assert.Equal(eventEntity.Id, firstReservation.EventId);
            Assert.Equal(tier.Id, firstReservation.TicketTypeId);
            Assert.Equal(100m, firstReservation.PriceAtReservation);
            Assert.InRange(firstReservation.ExpiresAt, before.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));

            var duplicateResponse = await ReserveAsync(first, eventEntity.Id, tier.Id);
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

            var secondReservation = await ReserveSuccessfullyAsync(second, eventEntity.Id, tier.Id);
            Assert.Equal(ReservationStatus.Held, secondReservation.Status);

            var publicResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            var publicEvent = await publicResponse.Content.ReadFromJsonAsync<EventDetailsResponse>(
                TestContext.Current.CancellationToken);
            var publicTier = publicEvent!.TicketTypes.Single();
            Assert.Equal(0, publicTier.Sold);
            Assert.Equal(2, publicTier.Held);
            Assert.Equal(0, publicTier.Remaining);

            var soldOutResponse = await ReserveAsync(third, eventEntity.Id, tier.Id);
            Assert.Equal(HttpStatusCode.Conflict, soldOutResponse.StatusCode);
            var soldOutError = await soldOutResponse.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal("conflict", soldOutError.GetProperty("code").GetString());

            var cancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{firstReservation.Id}",
                token: first.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

            var repurchaseResponse = await ReserveAsync(first, eventEntity.Id, tier.Id);
            Assert.Equal(HttpStatusCode.Conflict, repurchaseResponse.StatusCode);

            var replacementReservation = await ReserveSuccessfullyAsync(third, eventEntity.Id, tier.Id);
            Assert.Equal(ReservationStatus.Held, replacementReservation.Status);

            await using var db = TestDatabase.CreateDbContext();
            var reservations = await db.Reservations
                .Where(item => item.EventId == eventEntity.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, reservations.Count);
            Assert.Single(reservations, item => item.Status == ReservationStatus.Cancelled);
            Assert.Equal(2, reservations.Count(item => item.Status == ReservationStatus.Held));
        }

        [Fact]
        public async Task Reserve_RejectsInvalidEventTicketTypeAndTiming()
        {
            var organizer = await RegisterOrganizerAsync();
            var notOpenAttendee = await RegisterAttendeeAsync();

            var notOpenEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-not-open"),
                tiers: new[]
                {
                    new TicketTypeSpec(
                        "Later",
                        100m,
                        2,
                        SalesOpenAt: DateTime.UtcNow.AddHours(2))
                });
            var notOpenResponse = await ReserveAsync(
                notOpenAttendee,
                notOpenEvent.Id,
                Tier(notOpenEvent).Id);
            Assert.Equal(HttpStatusCode.Conflict, notOpenResponse.StatusCode);

            var pastEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-past"),
                startsAt: DateTime.UtcNow.AddMinutes(-1));
            var pastAttendee = await RegisterAttendeeAsync();
            var pastResponse = await ReserveAsync(pastAttendee, pastEvent.Id, Tier(pastEvent).Id);
            Assert.Equal(HttpStatusCode.Conflict, pastResponse.StatusCode);

            var draftEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-draft"),
                EventStatus.Draft);
            var draftAttendee = await RegisterAttendeeAsync();
            var draftResponse = await ReserveAsync(draftAttendee, draftEvent.Id, Tier(draftEvent).Id);
            Assert.Equal(HttpStatusCode.NotFound, draftResponse.StatusCode);

            var publishedEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-mismatch"));
            var otherEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-other"));
            var mismatchAttendee = await RegisterAttendeeAsync();
            var mismatchResponse = await ReserveAsync(
                mismatchAttendee,
                publishedEvent.Id,
                Tier(otherEvent).Id);
            Assert.Equal(HttpStatusCode.NotFound, mismatchResponse.StatusCode);

            var randomTicketTypeAttendee = await RegisterAttendeeAsync();
            var randomTicketTypeResponse = await ReserveAsync(
                randomTicketTypeAttendee,
                publishedEvent.Id,
                Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NotFound, randomTicketTypeResponse.StatusCode);

            var missingEventAttendee = await RegisterAttendeeAsync();
            var missingEventResponse = await ReserveAsync(
                missingEventAttendee,
                Guid.NewGuid(),
                Tier(publishedEvent).Id);
            Assert.Equal(HttpStatusCode.NotFound, missingEventResponse.StatusCode);

            await using (var db = TestDatabase.CreateDbContext())
            {
                var owner = await db.Users.SingleAsync(
                    user => user.Id == Guid.Parse(organizer.UserId),
                    TestContext.Current.CancellationToken);
                owner.IsSuspended = true;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var suspendedAttendee = await RegisterAttendeeAsync();
            var suspendedResponse = await ReserveAsync(
                suspendedAttendee,
                publishedEvent.Id,
                Tier(publishedEvent).Id);
            Assert.Equal(HttpStatusCode.NotFound, suspendedResponse.StatusCode);
        }

        [Fact]
        public async Task Cancel_IsIdempotentAndRejectsPaidForeignAndExpiredReservations()
        {
            var organizer = await RegisterOrganizerAsync();
            var owner = await RegisterAttendeeAsync();
            var stranger = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("cancel-rules"));
            var tier = Tier(eventEntity);

            var reservation = await ReserveSuccessfullyAsync(owner, eventEntity.Id, tier.Id);

            var foreignCancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{reservation.Id}",
                token: stranger.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignCancelResponse.StatusCode);

            var cancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{reservation.Id}",
                token: owner.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

            var repeatCancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{reservation.Id}",
                token: owner.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, repeatCancelResponse.StatusCode);

            var secondEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("cancel-paid"));
            var secondTier = Tier(secondEvent);
            var paidReservation = await ReserveSuccessfullyAsync(owner, secondEvent.Id, secondTier.Id);

            await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 500m },
                owner.AccessToken);
            var payResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/reservations/{paidReservation.Id}/payment",
                token: owner.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, payResponse.StatusCode);

            var payWithKeyResponse = await SendPayAsync(paidReservation.Id, owner.AccessToken, "cancel-paid-key");
            Assert.Equal(HttpStatusCode.OK, payWithKeyResponse.StatusCode);

            var paidCancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{paidReservation.Id}",
                token: owner.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, paidCancelResponse.StatusCode);

            var thirdEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("cancel-expired"));
            var thirdReservation = await ReserveSuccessfullyAsync(owner, thirdEvent.Id, Tier(thirdEvent).Id);
            await UpdateReservationAsync(thirdReservation.Id, item =>
            {
                item.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
                item.ExpiresAt = DateTime.UtcNow.AddMinutes(-10);
                item.Status = ReservationStatus.Held;
            });

            var expiredCancelResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/reservations/{thirdReservation.Id}",
                token: owner.AccessToken);
            Assert.Equal(HttpStatusCode.Gone, expiredCancelResponse.StatusCode);
            var expiredError = await expiredCancelResponse.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
            Assert.Equal("reservation_expired", expiredError.GetProperty("code").GetString());

            await using var db = TestDatabase.CreateDbContext();
            var cancelled = await db.Reservations.SingleAsync(
                item => item.Id == reservation.Id,
                TestContext.Current.CancellationToken);
            Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
        }

        [Fact]
        public async Task History_ListsOwnReservationsNewestFirstAndFlagsExpired()
        {
            var organizer = await RegisterOrganizerAsync();
            var owner = await RegisterAttendeeAsync();
            var stranger = await RegisterAttendeeAsync();
            var firstEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("history-first"));
            var secondEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("history-second"));
            var strangerEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("history-stranger"));

            var expiredReservation = await ReserveSuccessfullyAsync(
                owner,
                firstEvent.Id,
                Tier(firstEvent).Id);
            var paidReservation = await ReserveSuccessfullyAsync(
                owner,
                secondEvent.Id,
                Tier(secondEvent).Id);
            await ReserveSuccessfullyAsync(
                stranger,
                strangerEvent.Id,
                Tier(strangerEvent).Id);

            await UpdateReservationAsync(expiredReservation.Id, item =>
            {
                item.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
                item.ExpiresAt = DateTime.UtcNow.AddMinutes(-10);
                item.Status = ReservationStatus.Held;
            });

            await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 500m },
                owner.AccessToken);
            var payResponse = await SendPayAsync(paidReservation.Id, owner.AccessToken, "history-pay-key");
            Assert.Equal(HttpStatusCode.OK, payResponse.StatusCode);
            var payment = await payResponse.Content.ReadFromJsonAsync<PaymentResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);

            var response = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/reservations",
                owner.AccessToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var reservations = await response.Content.ReadFromJsonAsync<List<MyReservationResponse>>(
                JsonOptions,
                TestContext.Current.CancellationToken);

            Assert.NotNull(reservations);
            Assert.Equal(2, reservations!.Count);
            Assert.DoesNotContain(reservations, item => item.EventId == strangerEvent.Id);
            Assert.Equal(secondEvent.Id, reservations[0].EventId);
            Assert.Equal(firstEvent.Id, reservations[1].EventId);

            Assert.Equal(ReservationStatus.Paid, reservations[0].Status);
            Assert.False(reservations[0].IsExpired);
            Assert.Equal(payment!.TicketId, reservations[0].TicketId);
            Assert.Equal(payment.Code, reservations[0].TicketCode);
            Assert.NotNull(reservations[0].PaidAt);
            Assert.Equal("General", reservations[0].TicketTypeName);
            Assert.Equal(secondEvent.Title, reservations[0].EventTitle);
            Assert.Equal(secondEvent.Venue, reservations[0].Venue);

            Assert.Equal(ReservationStatus.Held, reservations[1].Status);
            Assert.True(reservations[1].IsExpired);
            Assert.Null(reservations[1].TicketId);
            Assert.Null(reservations[1].TicketCode);

            var strangerResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/reservations",
                stranger.AccessToken);
            var strangerReservations = await strangerResponse.Content.ReadFromJsonAsync<List<MyReservationResponse>>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Single(strangerReservations!);
            Assert.Equal(strangerEvent.Id, strangerReservations![0].EventId);
        }

        [Fact]
        public async Task ReservationEndpoints_RequireAttendeeAndRateLimitFivePerUser()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("reserve-permissions"));
            var tier = Tier(eventEntity);

            var anonymousResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/reservations",
                new { ticketTypeId = tier.Id });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            var organizerResponse = await ReserveAsync(organizer, eventEntity.Id, tier.Id);
            Assert.Equal(HttpStatusCode.Forbidden, organizerResponse.StatusCode);

            await ReserveSuccessfullyAsync(attendee, eventEntity.Id, tier.Id);

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var conflictResponse = await ReserveAsync(attendee, eventEntity.Id, tier.Id);
                Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
            }

            var limitedResponse = await ReserveAsync(attendee, eventEntity.Id, tier.Id);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        }

        private async Task<HttpResponseMessage> SendPayAsync(
            Guid reservationId,
            string token,
            string idempotencyKey)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/reservations/{reservationId}/payment");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token);
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());
            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }
}
