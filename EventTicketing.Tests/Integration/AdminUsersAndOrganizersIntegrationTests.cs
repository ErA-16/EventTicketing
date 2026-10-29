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
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class AdminUsersAndOrganizersIntegrationTests
    {
        private readonly HttpClient _client;

        public AdminUsersAndOrganizersIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        private static async Task<Event> CreatePublishedEventAsync(TestSession organizer, string title)
        {
            return await TestData.CreateEventAsync(
                Guid.Parse(organizer.UserId),
                title,
                EventStatus.Published,
                startsAt: DateTime.UtcNow.AddDays(5),
                imagePath: "events/test/cover.jpg",
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

        [Fact]
        public async Task OrganizerList_FiltersRoleSuspensionSearchAndPages()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var token = $"{TestRun.Prefix}admin-organizers";
            var first = await AuthHelper.RegisterOrganizerAsync(
                _client,
                email: $"{token}-1@example.invalid",
                organizationName: $"{token} One");
            var second = await AuthHelper.RegisterOrganizerAsync(
                _client,
                email: $"{token}-2@example.invalid",
                organizationName: $"{token} Two");
            var third = await AuthHelper.RegisterOrganizerAsync(
                _client,
                email: $"{token}-3@example.invalid",
                organizationName: $"{token} Three");
            var attendeeEmail = $"{token}-attendee@example.invalid";
            await AuthHelper.RegisterAttendeeAsync(_client, email: attendeeEmail);

            var firstPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/organizers?q={Uri.EscapeDataString(token)}&limit=2",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
            var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<CursorPage<AdminOrganizerResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, firstPage!.Items.Count);
            Assert.True(firstPage.HasMore);
            Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));
            Assert.All(firstPage.Items, item => Assert.Contains(token, item.OrganizationName));

            var secondPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/organizers?q={Uri.EscapeDataString(token)}&limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
            var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<CursorPage<AdminOrganizerResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Single(secondPage!.Items);
            Assert.False(secondPage.HasMore);
            Assert.Empty(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)));
            Assert.Equal(
                new[] { first.UserId, second.UserId, third.UserId }.OrderBy(id => id),
                firstPage.Items.Concat(secondPage.Items).Select(item => item.Id.ToString()).OrderBy(id => id));

            var attendeeSearchResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/organizers?q={Uri.EscapeDataString(attendeeEmail)}",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, attendeeSearchResponse.StatusCode);
            var attendeeSearch = await attendeeSearchResponse.Content.ReadFromJsonAsync<CursorPage<AdminOrganizerResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Empty(attendeeSearch!.Items);

            var suspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{second.UserId}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, suspendResponse.StatusCode);

            var suspendedResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/organizers?q={Uri.EscapeDataString(token)}&suspended=true",
                admin.AccessToken);
            var suspendedPage = await suspendedResponse.Content.ReadFromJsonAsync<CursorPage<AdminOrganizerResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Single(suspendedPage!.Items);
            Assert.Equal(second.UserId, suspendedPage.Items.Single().Id.ToString());
            Assert.True(suspendedPage.Items.Single().IsSuspended);

            var activeResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/organizers?q={Uri.EscapeDataString(token)}&suspended=false",
                admin.AccessToken);
            var activePage = await activeResponse.Content.ReadFromJsonAsync<CursorPage<AdminOrganizerResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, activePage!.Items.Count);
            Assert.DoesNotContain(activePage.Items, item => item.Id.ToString() == second.UserId);
            Assert.All(activePage.Items, item => Assert.False(item.IsSuspended));

            var invalidCursorResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/admin/organizers?cursor=not-base64",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidCursorResponse.StatusCode);

            var attendeeId = await GetUserIdAsync(attendeeEmail);
            var attendeeSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{attendeeId}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, attendeeSuspendResponse.StatusCode);

            var unknownSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{Guid.NewGuid()}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, unknownSuspendResponse.StatusCode);
        }

        [Fact]
        public async Task SuspendOrganizer_HidesEventsBlocksTokensAndRestoresOnUnsuspend()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var organizer = await AuthHelper.RegisterOrganizerAsync(
                _client,
                organizationName: $"Suspended Org {TestRun.Id}");
            var eventEntity = await CreatePublishedEventAsync(
                organizer,
                TestRun.NewTitle("admin-organizer-suspend"));

            var publicBeforeResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, publicBeforeResponse.StatusCode);

            var suspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{organizer.UserId}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, suspendResponse.StatusCode);

            var publicHiddenResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.NotFound, publicHiddenResponse.StatusCode);

            var oldTokenResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

            var loginResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/login",
                new { email = organizer.Email, password = organizer.Password });
            Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
            var loginError = await ReadJsonAsync(loginResponse);
            Assert.Equal("login_failed", loginError.GetProperty("code").GetString());
            Assert.Contains(
                loginError.GetProperty("details").EnumerateArray()
                    .Select(value => value.GetString()),
                detail => detail is not null
                    && detail.Contains("suspended", StringComparison.OrdinalIgnoreCase));

            var refreshResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/auth/refresh",
                new { refreshToken = organizer.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
            var refreshError = await ReadJsonAsync(refreshResponse);
            Assert.Equal("account_suspended", refreshError.GetProperty("code").GetString());

            var repeatedSuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{organizer.UserId}/suspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, repeatedSuspendResponse.StatusCode);

            var unsuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{organizer.UserId}/unsuspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, unsuspendResponse.StatusCode);

            var publicRestoredResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/events/{eventEntity.Id}");
            Assert.Equal(HttpStatusCode.OK, publicRestoredResponse.StatusCode);

            var oldTokenRestoredResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, oldTokenRestoredResponse.StatusCode);

            var loginRestoredResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/login",
                new { email = organizer.Email, password = organizer.Password });
            Assert.Equal(HttpStatusCode.OK, loginRestoredResponse.StatusCode);

            var repeatedUnsuspendResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/admin/organizers/{organizer.UserId}/unsuspend",
                token: admin.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, repeatedUnsuspendResponse.StatusCode);

            await using var db = TestDatabase.CreateDbContext();
            var eventStatus = await db.Events
                .Where(entity => entity.Id == eventEntity.Id)
                .Select(entity => entity.Status)
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(EventStatus.Published, eventStatus);
        }

        [Fact]
        public async Task UserList_SearchesRolesAndPages()
        {
            var admin = await AuthHelper.CreateAdminAsync(_client);
            var token = $"{TestRun.Prefix}admin-users";
            var attendeeOne = await AuthHelper.RegisterAttendeeAsync(
                _client,
                email: $"{token}-attendee-1@example.invalid",
                firstName: "Admin",
                lastName: "UserOne");
            var attendeeTwo = await AuthHelper.RegisterAttendeeAsync(
                _client,
                email: $"{token}-attendee-2@example.invalid",
                firstName: "Admin",
                lastName: "UserTwo");
            var organizer = await AuthHelper.RegisterOrganizerAsync(
                _client,
                email: $"{token}-organizer@example.invalid",
                organizationName: $"{token} Organization");

            var searchResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(token)}",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
            var searchPage = await searchResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(3, searchPage!.Items.Count);

            var attendee = searchPage.Items.Single(item => item.Id.ToString() == attendeeOne.UserId);
            Assert.Contains("Attendee", attendee.Roles);
            Assert.Equal("Admin UserOne", attendee.DisplayName);
            Assert.False(attendee.IsSuspended);

            var organizerItem = searchPage.Items.Single(item => item.Id.ToString() == organizer.UserId);
            Assert.Contains("Organizer", organizerItem.Roles);
            Assert.Equal($"{token} Organization", organizerItem.OrganizationName);
            Assert.Equal($"{token} Organization", organizerItem.DisplayName);

            var attendeeOnlyResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(token)}&role=attendee",
                admin.AccessToken);
            var attendeeOnly = await attendeeOnlyResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, attendeeOnly!.Items.Count);
            Assert.All(attendeeOnly.Items, item => Assert.Contains("Attendee", item.Roles));
            Assert.DoesNotContain(attendeeOnly.Items, item => item.Id.ToString() == organizer.UserId);

            var organizerOnlyResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(token)}&role=ORGANIZER",
                admin.AccessToken);
            var organizerOnly = await organizerOnlyResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Single(organizerOnly!.Items);
            Assert.Equal(organizer.UserId, organizerOnly.Items.Single().Id.ToString());

            var allRolesResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(token)}&role=All",
                admin.AccessToken);
            var allRoles = await allRolesResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(3, allRoles!.Items.Count);

            var invalidRoleResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/admin/users?role=Wizard",
                admin.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidRoleResponse.StatusCode);
            var invalidRoleError = await ReadJsonAsync(invalidRoleResponse);
            Assert.Contains(
                invalidRoleError.GetProperty("details").EnumerateArray()
                    .Select(value => value.GetString()),
                detail => detail == "Invalid role.");

            var pageToken = $"{TestRun.Prefix}admin-users-page";
            for (var index = 0; index < 3; index++)
            {
                await AuthHelper.RegisterAttendeeAsync(
                    _client,
                    email: $"{pageToken}-{index}@example.invalid");
            }

            var firstPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(pageToken)}&role=Attendee&limit=2",
                admin.AccessToken);
            var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, firstPage!.Items.Count);
            Assert.True(firstPage.HasMore);
            Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

            var secondPageResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/admin/users?q={Uri.EscapeDataString(pageToken)}&role=Attendee&limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}",
                admin.AccessToken);
            var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<CursorPage<AdminUserResponse>>(
                TestContext.Current.CancellationToken);
            Assert.Single(secondPage!.Items);
            Assert.False(secondPage.HasMore);
            Assert.Empty(firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)));

            Assert.Equal(
                new[] { attendeeOne.UserId, attendeeTwo.UserId }.OrderBy(id => id),
                searchPage.Items
                    .Where(item => item.Roles.Contains("Attendee"))
                    .Select(item => item.Id.ToString())
                    .OrderBy(id => id));
        }

        [Fact]
        public async Task AdminListsAndSuspension_RequireAdminRole()
        {
            var attendee = await AuthHelper.RegisterAttendeeAsync(_client);
            var organizer = await AuthHelper.RegisterOrganizerAsync(
                _client,
                organizationName: $"Admin List Permissions {TestRun.Id}");
            var paths = new[]
            {
                ("/api/v1/admin/users", HttpMethod.Get),
                ("/api/v1/admin/organizers", HttpMethod.Get),
                ($"/api/v1/admin/organizers/{organizer.UserId}/suspend", HttpMethod.Post),
                ($"/api/v1/admin/organizers/{organizer.UserId}/unsuspend", HttpMethod.Post)
            };

            foreach (var (path, method) in paths)
            {
                var anonymousResponse = await AuthHelper.SendJsonAsync(_client, method, path);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

                var attendeeResponse = await AuthHelper.SendJsonAsync(
                    _client,
                    method,
                    path,
                    token: attendee.AccessToken);
                Assert.Equal(HttpStatusCode.Forbidden, attendeeResponse.StatusCode);

                var organizerResponse = await AuthHelper.SendJsonAsync(
                    _client,
                    method,
                    path,
                    token: organizer.AccessToken);
                Assert.Equal(HttpStatusCode.Forbidden, organizerResponse.StatusCode);
            }
        }

        private static async Task<Guid> GetUserIdAsync(string email)
        {
            await using var db = TestDatabase.CreateDbContext();
            return await db.Users
                .Where(user => user.Email == email)
                .Select(user => user.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
        }
    }
}
