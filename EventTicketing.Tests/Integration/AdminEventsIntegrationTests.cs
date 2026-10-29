using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;

namespace EventTicketing.Tests.Integration
{
    public sealed class AdminEventsIntegrationTests
    {
        private readonly HttpClient _client;

        public AdminEventsIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        private static IReadOnlyList<string> GetDetails(JsonElement error)
        {
            if (!error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return details
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
        }

        private async Task<TestSession> RegisterOrganizerAsync(string organizationName)
        {
            return await AuthHelper.RegisterOrganizerAsync(
                _client,
                organizationName: organizationName);
        }

        private Task<Event> CreateEventAsync(
            TestSession organizer,
            string title,
            EventStatus status,
            DateTime? startsAt = null,
            string? imagePath = "events/test/cover.jpg")
        {
            return TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                status,
                startsAt: startsAt ?? DateTime.UtcNow.AddDays(5),
                imagePath: imagePath,
                ticketTypes: new[]
                {
                    new TicketTypeSpec(
                        "General",
                        100m,
                        10,
                        EarlyBirdPrice: 75m,
                        EarlyBirdEndsAt: DateTime.UtcNow.AddDays(2),
                        SalesOpenAt: DateTime.UtcNow.AddDays(-1))
                },
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private static string QueuePath(string query)
        {
            return $"/api/v1/admin/events?q={Uri.EscapeDataString(query)}";
        }

        [Fact]
        public async Task Queue_FiltersStatusesSearchesAndPages()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var organizer = await RegisterOrganizerAsync($"Admin Search {TestRun.Id}");
            var searchToken = $"{TestRun.Prefix}admin-queue";

            var pending = await CreateEventAsync(
                organizer,
                $"{searchToken}-pending",
                EventStatus.PendingApproval);
            var draft = await CreateEventAsync(
                organizer,
                $"{searchToken}-draft",
                EventStatus.Draft);
            var published = await CreateEventAsync(
                organizer,
                $"{searchToken}-published",
                EventStatus.Published);

            var defaultResponse = await AuthHelper.GetAsync(
                _client,
                QueuePath(searchToken),
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
            var defaultPage = await defaultResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);

            Assert.NotNull(defaultPage);
            Assert.Contains(defaultPage!.Items, item => item.Id == pending.Id);
            Assert.DoesNotContain(defaultPage.Items, item => item.Id == draft.Id);
            Assert.DoesNotContain(defaultPage.Items, item => item.Id == published.Id);

            var allResponse = await AuthHelper.GetAsync(
                _client,
                $"{QueuePath(searchToken)}&status=all",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
            var allPage = await allResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Contains(allPage!.Items, item => item.Id == pending.Id);
            Assert.Contains(allPage.Items, item => item.Id == draft.Id);
            Assert.Contains(allPage.Items, item => item.Id == published.Id);

            var organizationResponse = await AuthHelper.GetAsync(
                _client,
                QueuePath($"Admin Search {TestRun.Id}"),
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, organizationResponse.StatusCode);
            var organizationPage = await organizationResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Contains(organizationPage!.Items, item => item.Id == pending.Id);

            var invalidStatusResponse = await AuthHelper.GetAsync(
                _client,
                $"{QueuePath(searchToken)}&status=Nope",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
            var invalidStatusError = await ReadJsonAsync(invalidStatusResponse);
            Assert.Equal("validation_failed", invalidStatusError.GetProperty("code").GetString());

            var pageToken = $"{TestRun.Prefix}admin-page";
            var pageEvents = new List<Event>();
            for (var index = 0; index < 3; index++)
            {
                pageEvents.Add(await CreateEventAsync(
                    organizer,
                    $"{pageToken}-{index}",
                    EventStatus.PendingApproval));
            }

            var firstPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/events?q={Uri.EscapeDataString(pageToken)}&limit=2",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
            var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, firstPage!.Items.Count);
            Assert.True(firstPage.HasMore);
            Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

            var secondPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/events?q={Uri.EscapeDataString(pageToken)}&limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
            var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Single(secondPage!.Items);
            Assert.False(secondPage.HasMore);
            Assert.Empty(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)));
            Assert.Equal(
                pageEvents.Select(item => item.Id).OrderBy(id => id),
                firstPage.Items.Concat(secondPage.Items).Select(item => item.Id).OrderBy(id => id));
        }

        [Fact]
        public async Task Approve_OnlyPendingAndPublishEnablesPublicVisibility()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var organizer = await RegisterOrganizerAsync($"Admin Approve {TestRun.Id}");
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("admin-approve"),
                EventStatus.PendingApproval);

            var approveResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/approve",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
            var approved = await approveResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("Approved", approved!.Status);
            Assert.Equal(organizer.UserId, approved.OrganizerId.ToString());
            Assert.Equal($"Admin Approve {TestRun.Id}", approved.OrganizerName);
            Assert.Single(approved.TicketTypes);
            Assert.Equal(10, approved.TicketsLeft);

            var repeatApproveResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/approve",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, repeatApproveResponse.StatusCode);

            var missingApproveResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{Guid.NewGuid()}/approve",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, missingApproveResponse.StatusCode);

            var hiddenResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);

            var publishResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{eventEntity.Id}/publish",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

            var publicResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
            var publicEvent = await publicResponse.Content.ReadFromJsonAsync<EventDetailsResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal(eventEntity.Id, publicEvent!.Id);
            Assert.Equal(10, publicEvent.TicketTypes.Single().Remaining);
        }

        [Fact]
        public async Task Reject_RequiresReasonAndReturnsEventToDraft()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var organizer = await RegisterOrganizerAsync($"Admin Reject {TestRun.Id}");
            var eventEntity = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("admin-reject"),
                EventStatus.PendingApproval);

            var emptyReasonResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/reject",
                new { reason = "   " },
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, emptyReasonResponse.StatusCode);
            var emptyReasonError = await ReadJsonAsync(emptyReasonResponse);
            Assert.Contains(GetDetails(emptyReasonError), detail => detail == "A reason is required.");

            var longReasonResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/reject",
                new { reason = new string('x', 1001) },
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, longReasonResponse.StatusCode);
            var longReasonError = await ReadJsonAsync(longReasonResponse);
            Assert.Contains(
                GetDetails(longReasonError),
                detail => detail == "Reason must be 1000 characters or less.");

            var rejectResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/reject",
                new { reason = "  Needs a better cover.  " },
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
            var rejected = await rejectResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("Draft", rejected!.Status);
            Assert.Equal("Needs a better cover.", rejected.RejectionReason);

            var publicResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.NotFound, publicResponse.StatusCode);

            var updateResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{eventEntity.Id}",
                new
                {
                    title = $"{eventEntity.Title} Updated",
                    description = eventEntity.Description,
                    venue = eventEntity.Venue,
                    startsAt = new DateTimeOffset(eventEntity.StartsAt, TimeSpan.Zero)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            var submitResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{eventEntity.Id}/submit",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
            var resubmitted = await submitResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("PendingApproval", resubmitted!.Status);
            Assert.Null(resubmitted.RejectionReason);

            var draftEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("admin-reject-draft"),
                EventStatus.Draft);
            var rejectDraftResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{draftEvent.Id}/reject",
                new { reason = "Not pending." },
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, rejectDraftResponse.StatusCode);

            var missingRejectResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{Guid.NewGuid()}/reject",
                new { reason = "Not found." },
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, missingRejectResponse.StatusCode);
        }

        [Fact]
        public async Task SuspendUnsuspend_TransitionsAndPublicVisibility()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var organizer = await RegisterOrganizerAsync($"Admin Suspend {TestRun.Id}");
            var title = TestRun.NewTitle("admin-suspend");
            var eventEntity = await CreateEventAsync(
                organizer,
                title,
                EventStatus.Published);

            var publicBeforeResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, publicBeforeResponse.StatusCode);

            var suspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, suspendResponse.StatusCode);
            var suspended = await suspendResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("Suspended", suspended!.Status);

            var publicHiddenResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.NotFound, publicHiddenResponse.StatusCode);

            var queueResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/events?status=Suspended&q={Uri.EscapeDataString(title)}",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
            var queue = await queueResponse.Content.ReadFromJsonAsync<CursorPage<EventResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Contains(queue!.Items, item => item.Id == eventEntity.Id && item.Status == "Suspended");

            var repeatedSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, repeatedSuspendResponse.StatusCode);

            var unsuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{eventEntity.Id}/unsuspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, unsuspendResponse.StatusCode);
            var unsuspended = await unsuspendResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("Approved", unsuspended!.Status);

            var stillHiddenResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.NotFound, stillHiddenResponse.StatusCode);

            var publishResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{eventEntity.Id}/publish",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

            var publicRestoredResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, publicRestoredResponse.StatusCode);

            var draftEvent = await CreateEventAsync(
                organizer,
                TestRun.NewTitle("admin-suspend-draft"),
                EventStatus.Draft);
            var draftSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{draftEvent.Id}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, draftSuspendResponse.StatusCode);

            var nonSuspendedUnsuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{draftEvent.Id}/unsuspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, nonSuspendedUnsuspendResponse.StatusCode);

            var missingSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{Guid.NewGuid()}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, missingSuspendResponse.StatusCode);

            var missingUnsuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/events/{Guid.NewGuid()}/unsuspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, missingUnsuspendResponse.StatusCode);
        }

        [Fact]
        public async Task AdminEndpoints_RequireAdminRole()
        {
            var attendee = await AuthHelper.RegisterAttendeeAsync(_client);
            var organizer = await RegisterOrganizerAsync($"Admin Permissions {TestRun.Id}");
            var eventId = Guid.NewGuid();
            var paths = new[]
            {
                ("/api/v1/admin/events", HttpMethod.Get),
                ($"/api/v1/admin/events/{eventId}/approve", HttpMethod.Post),
                ($"/api/v1/admin/events/{eventId}/reject", HttpMethod.Post),
                ($"/api/v1/admin/events/{eventId}/suspend", HttpMethod.Post),
                ($"/api/v1/admin/events/{eventId}/unsuspend", HttpMethod.Post)
            };

            foreach (var (path, method) in paths)
            {
                var anonymousResponse = await AuthHelper.SendJsonAsync(
                    _client,
                    method,
                    path,
                    body: method == HttpMethod.Post && path.EndsWith("/reject", StringComparison.Ordinal)
                        ? new { reason = "Test reason." }
                        : null);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

                var attendeeResponse = await AuthHelper.SendJsonAsync(
                    _client,
                    method,
                    path,
                    body: method == HttpMethod.Post && path.EndsWith("/reject", StringComparison.Ordinal)
                        ? new { reason = "Test reason." }
                        : null,
                    token: attendee.AccessToken);
                Assert.Equal(HttpStatusCode.Forbidden, attendeeResponse.StatusCode);

                var organizerResponse = await AuthHelper.SendJsonAsync(
                    _client,
                    method,
                    path,
                    body: method == HttpMethod.Post && path.EndsWith("/reject", StringComparison.Ordinal)
                        ? new { reason = "Test reason." }
                        : null,
                    token: organizer.AccessToken);
                Assert.Equal(HttpStatusCode.Forbidden, organizerResponse.StatusCode);
            }
        }
    }
}
