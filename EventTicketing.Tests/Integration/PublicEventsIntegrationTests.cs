using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class PublicEventsIntegrationTests
    {
        private readonly HttpClient _client;
        private readonly FakeCacheService _cache;

        public PublicEventsIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
            _cache = fixture.Factory.Cache;
            _cache.Clear();
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        private static IReadOnlyList<JsonElement> GetItems(JsonElement root)
        {
            return root.GetProperty("items").EnumerateArray().ToList();
        }

        private async Task<HttpResponseMessage> GetAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());
            return await _client.SendAsync(request);
        }

        private async Task<Guid> RegisterOrganizerAsync(string organizationName = "Public Test Events")
        {
            var session = await AuthHelper.RegisterOrganizerAsync(
                _client,
                organizationName: organizationName);
            return Guid.Parse(session.UserId);
        }

        private async Task<string> RegisterAttendeeAsync()
        {
            var session = await AuthHelper.RegisterAttendeeAsync(_client);
            return session.UserId;
        }

        private static string EventUrl(string query)
        {
            return "/api/v1/events?q=" + Uri.EscapeDataString(query) + "&limit=50";
        }

        [Fact]
        public async Task Get_ReturnsOnlyPublishedFutureEventsFromActiveOrganizers()
        {
            var label = TestRun.NewTitle("public-visible");
            var organizerId = await RegisterOrganizerAsync();
            var suspendedOrganizerId = await RegisterOrganizerAsync();

            await using (var db = TestDatabase.CreateDbContext())
            {
                var suspendedOrganizer = await db.Users.SingleAsync(
                    user => user.Id == suspendedOrganizerId,
                    TestContext.Current.CancellationToken);
                suspendedOrganizer.IsSuspended = true;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var now = DateTime.UtcNow;
            var visible = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Visible",
                EventStatus.Published,
                now.AddDays(5),
                now,
                "Visible Venue",
                "covers/visible.jpg",
                new[]
                {
                    new TicketTypeSpec("General", 100m, 10, 75m, now.AddDays(5)),
                    new TicketTypeSpec("VIP", 200m, 5)
                },
                TestContext.Current.CancellationToken);

            await TestData.CreateEventAsync(organizerId, $"{label} Draft", EventStatus.Draft, now.AddDays(5), now, "Draft Venue", null, null, TestContext.Current.CancellationToken);
            await TestData.CreateEventAsync(organizerId, $"{label} Pending", EventStatus.PendingApproval, now.AddDays(5), now, "Pending Venue", null, null, TestContext.Current.CancellationToken);
            await TestData.CreateEventAsync(organizerId, $"{label} Approved", EventStatus.Approved, now.AddDays(5), now, "Approved Venue", null, null, TestContext.Current.CancellationToken);
            await TestData.CreateEventAsync(organizerId, $"{label} Suspended", EventStatus.Suspended, now.AddDays(5), now, "Suspended Venue", null, null, TestContext.Current.CancellationToken);
            await TestData.CreateEventAsync(organizerId, $"{label} Past", EventStatus.Published, now.AddDays(-1), now, "Past Venue", null, null, TestContext.Current.CancellationToken);
            await TestData.CreateEventAsync(suspendedOrganizerId, $"{label} Suspended Organizer", EventStatus.Published, now.AddDays(5), now, "Hidden Venue", null, null, TestContext.Current.CancellationToken);

            var response = await GetAsync(EventUrl(label));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var root = await ReadJsonAsync(response);
            var items = GetItems(root);

            var item = Assert.Single(items);
            Assert.Equal(visible.Id, item.GetProperty("id").GetGuid());
            Assert.Equal($"{label} Visible", item.GetProperty("title").GetString());
            Assert.Equal("Visible Venue", item.GetProperty("venue").GetString());
            Assert.Equal("https://example.invalid/covers/visible.jpg", item.GetProperty("imageUrl").GetString());
            Assert.Equal(75m, item.GetProperty("minPrice").GetDecimal());
            Assert.Equal(15, item.GetProperty("ticketsLeft").GetInt32());
            Assert.InRange(
                item.GetProperty("startsAt").GetDateTime(),
                now.AddDays(5).AddSeconds(-1),
                now.AddDays(5).AddSeconds(1));
        }

        [Fact]
        public async Task Get_SearchMatchesTitleAndVenueCaseInsensitively()
        {
            var label = TestRun.NewTitle("public-search");
            var organizerId = await RegisterOrganizerAsync();
            var now = DateTime.UtcNow;

            var titleMatch = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Lagos Night",
                EventStatus.Published,
                now.AddDays(3),
                now,
                "Main Hall",
                null,
                null,
                TestContext.Current.CancellationToken);

            var venueMatch = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Quiet Event",
                EventStatus.Published,
                now.AddDays(4),
                now.AddMinutes(-1),
                "ZetaVenue",
                null,
                null,
                TestContext.Current.CancellationToken);

            await TestData.CreateEventAsync(
                organizerId,
                $"{label} Other Event",
                EventStatus.Published,
                now.AddDays(5),
                now.AddMinutes(-2),
                "Elsewhere",
                null,
                null,
                TestContext.Current.CancellationToken);

            var titleResponse = await GetAsync(EventUrl($"{label} LAGOS"));
            Assert.Equal(HttpStatusCode.OK, titleResponse.StatusCode);
            var titleItems = GetItems(await ReadJsonAsync(titleResponse));
            var titleItem = Assert.Single(titleItems);
            Assert.Equal(titleMatch.Id, titleItem.GetProperty("id").GetGuid());

            var venueResponse = await GetAsync(EventUrl("zetavenue"));
            Assert.Equal(HttpStatusCode.OK, venueResponse.StatusCode);
            var venueItems = GetItems(await ReadJsonAsync(venueResponse));
            var venueItem = Assert.Single(venueItems);
            Assert.Equal(venueMatch.Id, venueItem.GetProperty("id").GetGuid());

            var noMatchResponse = await GetAsync(EventUrl(TestRun.NewTitle("public-no-match")));
            Assert.Equal(HttpStatusCode.OK, noMatchResponse.StatusCode);
            var noMatchRoot = await ReadJsonAsync(noMatchResponse);
            Assert.Empty(GetItems(noMatchRoot));
            Assert.False(noMatchRoot.GetProperty("hasMore").GetBoolean());
            Assert.Equal(JsonValueKind.Null, noMatchRoot.GetProperty("nextCursor").ValueKind);
        }

        [Fact]
        public async Task Get_CursorPagination_ReturnsNoDuplicatesAndCorrectHasMore()
        {
            var label = TestRun.NewTitle("public-cursor");
            var organizerId = await RegisterOrganizerAsync();
            var now = DateTime.UtcNow;

            var first = await TestData.CreateEventAsync(
                organizerId,
                $"{label} First",
                EventStatus.Published,
                now.AddDays(3),
                now,
                "Cursor Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            var second = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Second",
                EventStatus.Published,
                now.AddDays(4),
                now.AddMinutes(-1),
                "Cursor Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            var third = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Third",
                EventStatus.Published,
                now.AddDays(5),
                now.AddMinutes(-2),
                "Cursor Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            var pageOneResponse = await GetAsync(
                $"/api/v1/events?q={Uri.EscapeDataString(label)}&limit=1");
            Assert.Equal(HttpStatusCode.OK, pageOneResponse.StatusCode);
            var pageOne = await ReadJsonAsync(pageOneResponse);

            var pageOneItem = Assert.Single(GetItems(pageOne));
            Assert.Equal(first.Id, pageOneItem.GetProperty("id").GetGuid());
            Assert.True(pageOne.GetProperty("hasMore").GetBoolean());
            var nextCursor = pageOne.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(nextCursor));

            var pageTwoResponse = await GetAsync(
                $"/api/v1/events?q={Uri.EscapeDataString(label)}&limit=1&cursor={Uri.EscapeDataString(nextCursor!)}");
            Assert.Equal(HttpStatusCode.OK, pageTwoResponse.StatusCode);
            var pageTwo = await ReadJsonAsync(pageTwoResponse);

            var pageTwoItem = Assert.Single(GetItems(pageTwo));
            Assert.Equal(second.Id, pageTwoItem.GetProperty("id").GetGuid());
            Assert.True(pageTwo.GetProperty("hasMore").GetBoolean());
            var secondCursor = pageTwo.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(secondCursor));

            var pageThreeResponse = await GetAsync(
                $"/api/v1/events?q={Uri.EscapeDataString(label)}&limit=1&cursor={Uri.EscapeDataString(secondCursor!)}");
            Assert.Equal(HttpStatusCode.OK, pageThreeResponse.StatusCode);
            var pageThree = await ReadJsonAsync(pageThreeResponse);

            var pageThreeItem = Assert.Single(GetItems(pageThree));
            Assert.Equal(third.Id, pageThreeItem.GetProperty("id").GetGuid());
            Assert.False(pageThree.GetProperty("hasMore").GetBoolean());
            Assert.Equal(JsonValueKind.Null, pageThree.GetProperty("nextCursor").ValueKind);

            var seen = new[] { pageOneItem, pageTwoItem, pageThreeItem }
                .Select(item => item.GetProperty("id").GetGuid())
                .ToList();
            Assert.Equal(3, seen.Distinct().Count());
        }

        [Fact]
        public async Task GetById_ReturnsDetailsWithLiveStockOverlay()
        {
            var label = TestRun.NewTitle("public-stock");
            var organizerId = await RegisterOrganizerAsync("Stock Test Events");
            var now = DateTime.UtcNow;

            var eventEntity = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Stock Event",
                EventStatus.Published,
                now.AddDays(5),
                now,
                "Stock Venue",
                "covers/stock.jpg",
                new[]
                {
                    new TicketTypeSpec("General", 100m, 10, 80m, now.AddDays(3))
                },
                TestContext.Current.CancellationToken);

            var ticketTypeId = eventEntity.TicketTypes.Single().Id;
            var paidUserId = Guid.Parse(await RegisterAttendeeAsync());
            var heldUserId = Guid.Parse(await RegisterAttendeeAsync());

            var paid = await TestData.CreateReservationAsync(
                paidUserId,
                eventEntity.Id,
                ticketTypeId,
                ReservationStatus.Paid,
                cancellationToken: TestContext.Current.CancellationToken);

            var held = await TestData.CreateReservationAsync(
                heldUserId,
                eventEntity.Id,
                ticketTypeId,
                ReservationStatus.Held,
                DateTime.UtcNow.AddMinutes(10),
                cancellationToken: TestContext.Current.CancellationToken);

            var detailResponse = await GetAsync($"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await ReadJsonAsync(detailResponse);

            Assert.Equal(eventEntity.Id, detail.GetProperty("id").GetGuid());
            Assert.Equal("Stock Test Events", detail.GetProperty("organizerName").GetString());
            Assert.Equal("https://example.invalid/covers/stock.jpg", detail.GetProperty("imageUrl").GetString());

            var ticketType = Assert.Single(detail.GetProperty("ticketTypes").EnumerateArray());
            Assert.Equal(1, ticketType.GetProperty("sold").GetInt32());
            Assert.Equal(1, ticketType.GetProperty("held").GetInt32());
            Assert.Equal(8, ticketType.GetProperty("remaining").GetInt32());
            Assert.Equal(80m, ticketType.GetProperty("currentPrice").GetDecimal());

            var listResponse = await GetAsync(EventUrl($"{label} Stock Event"));
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var listRoot = await ReadJsonAsync(listResponse);
            var listItem = Assert.Single(GetItems(listRoot));
            Assert.Equal(8, listItem.GetProperty("ticketsLeft").GetInt32());

            await using (var db = TestDatabase.CreateDbContext())
            {
                var heldReservation = await db.Reservations.SingleAsync(
                    reservation => reservation.Id == held.Id,
                    TestContext.Current.CancellationToken);
                heldReservation.Status = ReservationStatus.Cancelled;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var refreshedDetailResponse = await GetAsync($"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, refreshedDetailResponse.StatusCode);
            var refreshedDetail = await ReadJsonAsync(refreshedDetailResponse);
            var refreshedTicketType = Assert.Single(refreshedDetail.GetProperty("ticketTypes").EnumerateArray());
            Assert.Equal(1, refreshedTicketType.GetProperty("sold").GetInt32());
            Assert.Equal(0, refreshedTicketType.GetProperty("held").GetInt32());
            Assert.Equal(9, refreshedTicketType.GetProperty("remaining").GetInt32());

            var refreshedListResponse = await GetAsync(EventUrl($"{label} Stock Event"));
            Assert.Equal(HttpStatusCode.OK, refreshedListResponse.StatusCode);
            var refreshedList = Assert.Single(GetItems(await ReadJsonAsync(refreshedListResponse)));
            Assert.Equal(9, refreshedList.GetProperty("ticketsLeft").GetInt32());
            Assert.NotEqual(paid.Id, held.Id);
        }

        [Fact]
        public async Task GetById_HidesUnavailableEvents()
        {
            var label = TestRun.NewTitle("public-hidden");
            var organizerId = await RegisterOrganizerAsync();
            var suspendedOrganizerId = await RegisterOrganizerAsync();
            var now = DateTime.UtcNow;

            var draft = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Draft",
                EventStatus.Draft,
                now.AddDays(3),
                now,
                "Hidden Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            var past = await TestData.CreateEventAsync(
                organizerId,
                $"{label} Past",
                EventStatus.Published,
                now.AddDays(-1),
                now,
                "Hidden Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            var suspendedOrganizerEvent = await TestData.CreateEventAsync(
                suspendedOrganizerId,
                $"{label} Suspended Organizer",
                EventStatus.Published,
                now.AddDays(3),
                now,
                "Hidden Venue",
                null,
                null,
                TestContext.Current.CancellationToken);

            await using (var db = TestDatabase.CreateDbContext())
            {
                var suspendedOrganizer = await db.Users.SingleAsync(
                    user => user.Id == suspendedOrganizerId,
                    TestContext.Current.CancellationToken);
                suspendedOrganizer.IsSuspended = true;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            foreach (var eventId in new[] { draft.Id, past.Id, suspendedOrganizerEvent.Id })
            {
                var response = await GetAsync($"/api/v1/events/{eventId}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

                var error = await ReadJsonAsync(response);
                Assert.Equal("not_found", error.GetProperty("code").GetString());
            }
        }

        [Fact]
        public async Task Get_InvalidCursor_ReturnsValidationFailure()
        {
            var response = await GetAsync("/api/v1/events?cursor=not-base64");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await ReadJsonAsync(response);
            Assert.Equal("validation_failed", error.GetProperty("code").GetString());
        }
    }
}
