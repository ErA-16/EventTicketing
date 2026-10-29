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
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class TicketIntegrationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _client;

        public TicketIntegrationTests(ApiFixture fixture)
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
            DateTime startsAt,
            string venue = "Test Venue",
            string tierName = "General")
        {
            return await TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                EventStatus.Published,
                startsAt: startsAt,
                venue: venue,
                ticketTypes: new[]
                {
                    new TicketTypeSpec(
                        tierName,
                        100m,
                        5,
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

        private async Task<PaymentResponse> BuyAsync(
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

            var response = await _client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Purchase failed with {(int)response.StatusCode}. Body: {body}");
            }

            return (await response.Content.ReadFromJsonAsync<PaymentResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private async Task<ReservationResponse> ReserveAsync(
            TestSession attendee,
            Event eventEntity)
        {
            var response = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/events/{eventEntity.Id}/reservations",
                new { ticketTypeId = Tier(eventEntity).Id },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ReservationResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private Task<HttpResponseMessage> CheckInAsync(
            string code,
            string? token)
        {
            return AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/tickets/check-in",
                new { code },
                token);
        }

        private async Task<CheckInTicketResponse> ReadCheckInAsync(
            HttpResponseMessage response)
        {
            return (await response.Content.ReadFromJsonAsync<CheckInTicketResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private async Task<CursorPage<MyTicketResponse>> GetMyTicketsAsync(
            string token,
            string? query = null)
        {
            var response = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/tickets" + (query ?? string.Empty),
                token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<CursorPage<MyTicketResponse>>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private async Task<List<MyReservationResponse>> GetMyReservationsAsync(
            string token)
        {
            var response = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/reservations",
                token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<List<MyReservationResponse>>(
                JsonOptions,
                TestContext.Current.CancellationToken))!;
        }

        private static void AssertClose(DateTime expected, DateTime? actual)
        {
            Assert.NotNull(actual);
            Assert.True(
                Math.Abs((expected - actual.Value).TotalMilliseconds) < 1,
                $"Expected {expected:O} but got {actual.Value:O}.");
        }

        private static async Task SetEventStatusAsync(
            Guid eventId,
            EventStatus status)
        {
            await using var db = TestDatabase.CreateDbContext();
            var eventEntity = await db.Events.SingleAsync(
                item => item.Id == eventId,
                TestContext.Current.CancellationToken);
            eventEntity.Status = status;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task CheckIn_ValidatesAccessAndAcceptsOwnerCodeCaseAndWhitespace()
        {
            var organizer = await RegisterOrganizerAsync();
            var otherOrganizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("checkin"),
                DateTime.UtcNow.AddDays(2));
            await FundAsync(attendee, 100m);
            var payment = await BuyAsync(attendee, eventEntity, "checkin-buy");

            var blankResponse = await CheckInAsync(string.Empty, organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, blankResponse.StatusCode);

            var anonymousResponse = await CheckInAsync(payment.Code, null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            var attendeeResponse = await CheckInAsync(payment.Code, attendee.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, attendeeResponse.StatusCode);

            var foreignResponse = await CheckInAsync(
                payment.Code,
                otherOrganizer.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

            var unknownResponse = await CheckInAsync(
                "AAAAAAA2",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);

            var successResponse = await CheckInAsync(
                $"  {payment.Code.ToLowerInvariant()}  ",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
            var checkIn = await ReadCheckInAsync(successResponse);
            Assert.Equal(payment.TicketId, checkIn.TicketId);
            Assert.Equal(payment.Code, checkIn.Code);
            Assert.Equal(eventEntity.Id, checkIn.EventId);
            Assert.Equal(eventEntity.Title, checkIn.EventTitle);
            Assert.NotEqual(default, checkIn.CheckedInAt);

            var duplicateResponse = await CheckInAsync(
                payment.Code,
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

            await using var db = TestDatabase.CreateDbContext();
            var ticket = await db.Tickets.SingleAsync(
                item => item.Id == payment.TicketId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(ticket.CheckedInAt);
            AssertClose(checkIn.CheckedInAt, ticket.CheckedInAt);
        }

        [Fact]
        public async Task CheckIn_AllowsSuspendedEventAndExactlyOneConcurrentWinner()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("checkin-race"),
                DateTime.UtcNow.AddDays(2));
            await FundAsync(attendee, 100m);
            var payment = await BuyAsync(attendee, eventEntity, "checkin-race-buy");

            await SetEventStatusAsync(eventEntity.Id, EventStatus.Suspended);

            var first = CheckInAsync(payment.Code, organizer.AccessToken);
            var second = CheckInAsync(payment.Code, organizer.AccessToken);
            var responses = await Task.WhenAll(first, second);

            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

            await using var db = TestDatabase.CreateDbContext();
            var tickets = await db.Tickets
                .Where(item => item.EventId == eventEntity.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
            var ticket = Assert.Single(tickets);
            Assert.NotNull(ticket.CheckedInAt);
        }

        [Fact]
        public async Task MyTickets_ListsOnlyPaidOwnTicketsInStartOrderAndShowsCheckIn()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();
            var otherAttendee = await RegisterAttendeeAsync();
            var emptyAttendee = await RegisterAttendeeAsync();

            var laterEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-later"),
                DateTime.UtcNow.AddDays(5),
                venue: "Later Venue",
                tierName: "Later Tier");
            var earlierEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-earlier"),
                DateTime.UtcNow.AddDays(2),
                venue: "Earlier Venue",
                tierName: "Earlier Tier");
            var heldEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-held"),
                DateTime.UtcNow.AddDays(3));
            var otherEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-other"),
                DateTime.UtcNow.AddDays(1));

            await FundAsync(attendee, 500m);
            var laterPayment = await BuyAsync(
                attendee,
                laterEvent,
                "mytickets-later-buy");
            var earlierPayment = await BuyAsync(
                attendee,
                earlierEvent,
                "mytickets-earlier-buy");
            await ReserveAsync(attendee, heldEvent);

            await FundAsync(otherAttendee, 100m);
            var otherPayment = await BuyAsync(
                otherAttendee,
                otherEvent,
                "mytickets-other-buy");

            Assert.Empty((await GetMyTicketsAsync(emptyAttendee.AccessToken)).Items);

            var checkInResponse = await CheckInAsync(
                earlierPayment.Code,
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, checkInResponse.StatusCode);
            var checkIn = await ReadCheckInAsync(checkInResponse);

            var ticketPage = await GetMyTicketsAsync(attendee.AccessToken);
            var tickets = ticketPage.Items;
            Assert.Equal(2, tickets.Count);
            Assert.False(ticketPage.HasMore);
            Assert.Null(ticketPage.NextCursor);
            Assert.Equal(earlierPayment.TicketId, tickets[0].TicketId);
            Assert.Equal(laterPayment.TicketId, tickets[1].TicketId);
            Assert.DoesNotContain(tickets, item => item.Code == otherPayment.Code);

            var earlier = tickets[0];
            Assert.Equal(earlierPayment.Code, earlier.Code);
            Assert.Equal(earlierEvent.Id, earlier.EventId);
            Assert.Equal(earlierEvent.Title, earlier.EventTitle);
            Assert.Equal("Earlier Venue", earlier.Venue);
            Assert.Equal("Earlier Tier", earlier.TicketTypeName);
            Assert.Equal(100m, earlier.PricePaid);
            Assert.NotNull(earlier.PaidAt);
            AssertClose(checkIn.CheckedInAt, earlier.CheckedInAt);
            Assert.Equal("Test Organization", earlier.OrganizerName);

            Assert.Null(tickets[1].CheckedInAt);

            var otherTickets = await GetMyTicketsAsync(otherAttendee.AccessToken);
            var otherTicket = Assert.Single(otherTickets.Items);
            Assert.Equal(otherPayment.TicketId, otherTicket.TicketId);

            var reservations = await GetMyReservationsAsync(attendee.AccessToken);
            var earlierReservation = Assert.Single(
                reservations,
                item => item.Id == earlierPayment.ReservationId);
            Assert.Equal(ReservationStatus.Paid, earlierReservation.Status);
            AssertClose(checkIn.CheckedInAt, earlierReservation.CheckedInAt);

            var noTokenResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/tickets");
            Assert.Equal(HttpStatusCode.Unauthorized, noTokenResponse.StatusCode);

            var organizerResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/tickets",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, organizerResponse.StatusCode);
        }

        [Fact]
        public async Task MyTickets_PaginatesInStableOrderAndRejectsBadCursor()
        {
            var organizer = await RegisterOrganizerAsync();
            var attendee = await RegisterAttendeeAsync();

            var firstEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-page-first"),
                DateTime.UtcNow.AddDays(2));
            var secondEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-page-second"),
                DateTime.UtcNow.AddDays(3));
            var thirdEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("mytickets-page-third"),
                DateTime.UtcNow.AddDays(4));

            await FundAsync(attendee, 500m);
            var firstPayment = await BuyAsync(
                attendee,
                firstEvent,
                "mytickets-page-first-buy");
            var secondPayment = await BuyAsync(
                attendee,
                secondEvent,
                "mytickets-page-second-buy");
            var thirdPayment = await BuyAsync(
                attendee,
                thirdEvent,
                "mytickets-page-third-buy");

            var firstPage = await GetMyTicketsAsync(attendee.AccessToken, "?limit=2");
            Assert.Equal(2, firstPage.Items.Count);
            Assert.True(firstPage.HasMore);
            Assert.NotNull(firstPage.NextCursor);

            var secondPage = await GetMyTicketsAsync(
                attendee.AccessToken,
                $"?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
            Assert.Single(secondPage.Items);
            Assert.False(secondPage.HasMore);
            Assert.Null(secondPage.NextCursor);

            var combined = firstPage.Items.Concat(secondPage.Items).ToList();
            Assert.Equal(
                new[]
                {
                    firstPayment.TicketId,
                    secondPayment.TicketId,
                    thirdPayment.TicketId
                },
                combined.Select(ticket => ticket.TicketId).ToArray());
            Assert.Equal(3, combined.Select(ticket => ticket.TicketId).Distinct().Count());

            var invalidCursorResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/me/tickets?cursor=not-base64",
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidCursorResponse.StatusCode);
        }

        [Fact]
        public async Task CheckIn_RateLimitIsSixtyPerMinute()
        {
            var organizer = await RegisterOrganizerAsync();

            for (var attempt = 0; attempt < 60; attempt++)
            {
                var response = await CheckInAsync(
                    "AAAAAAA2",
                    organizer.AccessToken);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            var limitedResponse = await CheckInAsync(
                "AAAAAAA2",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        }
    }
}
