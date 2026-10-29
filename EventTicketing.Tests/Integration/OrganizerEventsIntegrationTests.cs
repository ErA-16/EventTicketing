using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class OrganizerEventsIntegrationTests
    {
        private readonly HttpClient _client;
        private readonly FakeImageStorage _images;

        public OrganizerEventsIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
            _images = fixture.Factory.Images;
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

        private async Task<TestSession> RegisterOrganizerAsync(string organizationName = "Test Organizers")
        {
            return await AuthHelper.RegisterOrganizerAsync(_client, organizationName: organizationName);
        }

        private async Task<TestSession> RegisterAttendeeAsync()
        {
            return await AuthHelper.RegisterAttendeeAsync(_client);
        }

        private async Task<EventResponse> CreateEventAsync(
            TestSession organizer,
            string title,
            DateTimeOffset? startsAt = null,
            string venue = "Test Venue")
        {
            var response = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/organizer/events",
                new
                {
                    title,
                    description = $"Description for {title}",
                    venue,
                    startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(5)
                },
                organizer.AccessToken);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Event creation failed with {(int)response.StatusCode}. Body: {body}");
            }

            return await response.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("Event creation returned no body.");
        }

        private static Task SetStatusAsync(Guid eventId, EventStatus status)
        {
            return SetStatusCoreAsync(eventId, status);
        }

        private static async Task SetStatusCoreAsync(Guid eventId, EventStatus status)
        {
            await using var db = TestDatabase.CreateDbContext();
            var eventEntity = await db.Events.SingleAsync(
                entity => entity.Id == eventId,
                TestContext.Current.CancellationToken);
            eventEntity.Status = status;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private async Task<HttpResponseMessage> UploadImageAsync(
            Guid eventId,
            string token,
            byte[]? bytes = null,
            string contentType = "image/jpeg",
            bool includeFile = true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/organizer/events/{eventId}/image");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());

            using var form = new MultipartFormDataContent();
            if (includeFile)
            {
                var image = new ByteArrayContent(bytes ?? new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });
                image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                form.Add(image, "image", "cover.jpg");
            }

            request.Content = form;
            return await _client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Create_ReturnsDraftAndVersionedLocation()
        {
            var organizer = await RegisterOrganizerAsync("Draft Makers");
            var otherOrganizer = await RegisterOrganizerAsync("Other Draft Makers");
            var title = TestRun.NewTitle("organizer-create");

            var startsAt = DateTimeOffset.UtcNow.AddDays(5);
            var response = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/organizer/events",
                new
                {
                    title,
                    description = "A valid event description.",
                    venue = "Create Venue",
                    startsAt
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Contains(
                $"/api/v1/organizer/events/",
                response.Headers.Location?.ToString());

            var created = await response.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created!.Id);
            Assert.Equal(organizer.UserId, created.OrganizerId.ToString());
            Assert.Equal("Draft Makers", created.OrganizerName);
            Assert.Equal(title, created.Title);
            Assert.Equal("Draft", created.Status);
            Assert.Null(created.ImageUrl);
            Assert.Equal(0, created.TicketsLeft);
            Assert.Empty(created.TicketTypes);
            Assert.Equal(startsAt.UtcDateTime, created.StartsAt, TimeSpan.FromSeconds(1));
            Assert.Contains(created.Id.ToString(), response.Headers.Location!.ToString());

            var ownResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/organizer/events/{created.Id}",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
            var own = await ownResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal(created.Id, own!.Id);

            var otherEvent = await CreateEventAsync(otherOrganizer, TestRun.NewTitle("organizer-other"));

            var mineResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, mineResponse.StatusCode);
            var mine = await mineResponse.Content.ReadFromJsonAsync<List<EventResponse>>(
                TestContext.Current.CancellationToken);

            Assert.NotNull(mine);
            Assert.Contains(mine!, item => item.Id == created.Id);
            Assert.DoesNotContain(mine!, item => item.Id == otherEvent.Id);

            var foreignResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/organizer/events/{otherEvent.Id}",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        }

        [Fact]
        public async Task Create_RejectsPastStartAndInvalidFields()
        {
            var organizer = await RegisterOrganizerAsync();
            var pastStart = DateTimeOffset.UtcNow.AddMinutes(-1);

            var pastResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/organizer/events",
                new
                {
                    title = TestRun.NewTitle("organizer-past"),
                    description = "A valid event description.",
                    venue = "Past Venue",
                    startsAt = pastStart
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.BadRequest, pastResponse.StatusCode);
            var pastError = await ReadJsonAsync(pastResponse);
            Assert.Equal("validation_failed", pastError.GetProperty("code").GetString());
            Assert.Contains(GetDetails(pastError), detail => detail == "StartsAt must be in the future.");

            var shortTitleResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/organizer/events",
                new
                {
                    title = "ab",
                    description = "A valid event description.",
                    venue = "Short Venue",
                    startsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.BadRequest, shortTitleResponse.StatusCode);
            var shortTitleError = await ReadJsonAsync(shortTitleResponse);
            Assert.Equal("validation_failed", shortTitleError.GetProperty("code").GetString());
        }

        [Fact]
        public async Task Update_IsDraftOnlyAndOwnerScoped()
        {
            var organizer = await RegisterOrganizerAsync("Update Owners");
            var otherOrganizer = await RegisterOrganizerAsync("Other Update Owners");
            var created = await CreateEventAsync(organizer, TestRun.NewTitle("organizer-update"));
            var newStart = DateTimeOffset.UtcNow.AddDays(8);

            var updateResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}",
                new
                {
                    title = $"{created.Title} Updated",
                    description = "Updated description for the event.",
                    venue = "Updated Venue",
                    startsAt = newStart
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await updateResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal($"{created.Title} Updated", updated!.Title);
            Assert.Equal("Updated Venue", updated.Venue);
            Assert.Equal("Draft", updated.Status);
            Assert.Equal(newStart.UtcDateTime, updated.StartsAt, TimeSpan.FromSeconds(1));

            var foreignResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}",
                new
                {
                    title = "Foreign Update",
                    description = "Updated description for the event.",
                    venue = "Foreign Venue",
                    startsAt = newStart
                },
                otherOrganizer.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

            await SetStatusAsync(created.Id, EventStatus.PendingApproval);

            var lockedResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}",
                new
                {
                    title = "Locked Update",
                    description = "Updated description for the event.",
                    venue = "Locked Venue",
                    startsAt = newStart
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, lockedResponse.StatusCode);

            await SetStatusAsync(created.Id, EventStatus.Draft);

            var pastResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}",
                new
                {
                    title = "Past Update",
                    description = "Updated description for the event.",
                    venue = "Past Venue",
                    startsAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, pastResponse.StatusCode);
            var pastError = await ReadJsonAsync(pastResponse);
            Assert.Contains(GetDetails(pastError), detail => detail == "StartsAt must be in the future.");
        }

        [Fact]
        public async Task TicketTypes_ValidateRulesAndDraftOwnership()
        {
            var organizer = await RegisterOrganizerAsync("Ticket Type Owners");
            var otherOrganizer = await RegisterOrganizerAsync("Other Ticket Type Owners");
            var startsAt = DateTimeOffset.UtcNow.AddDays(5);
            var created = await CreateEventAsync(organizer, TestRun.NewTitle("organizer-ticket-type"), startsAt);

            var validResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "General",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.Created, validResponse.StatusCode);
            Assert.Contains(
                $"/api/v1/organizer/events/{created.Id}",
                validResponse.Headers.Location?.ToString());

            var valid = await validResponse.Content.ReadFromJsonAsync<TicketTypeResponse>(
                TestContext.Current.CancellationToken);
            Assert.NotNull(valid);
            Assert.Equal("General", valid!.Name);
            Assert.Equal(10, valid.Quantity);
            Assert.Equal(0, valid.Sold);
            Assert.Equal(0, valid.Held);
            Assert.Equal(10, valid.Remaining);

            var missingPairResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Missing Pair",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = (DateTimeOffset?)null
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingPairResponse.StatusCode);
            var missingPairError = await ReadJsonAsync(missingPairResponse);
            Assert.Contains(
                GetDetails(missingPairError),
                detail => detail == "EarlyBirdPrice and EarlyBirdEndsAt must be provided together.");

            var highEarlyBirdResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "High Early Bird",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 100m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, highEarlyBirdResponse.StatusCode);
            var highEarlyBirdError = await ReadJsonAsync(highEarlyBirdResponse);
            Assert.Contains(
                GetDetails(highEarlyBirdError),
                detail => detail == "EarlyBirdPrice must be lower than Price.");

            var lateSalesResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Late Sales",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = startsAt,
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, lateSalesResponse.StatusCode);
            var lateSalesError = await ReadJsonAsync(lateSalesResponse);
            Assert.Contains(
                GetDetails(lateSalesError),
                detail => detail == "SalesOpenAt must be before the event starts.");

            var lateEarlyBirdResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Late Early Bird",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = startsAt.AddMinutes(1)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, lateEarlyBirdResponse.StatusCode);
            var lateEarlyBirdError = await ReadJsonAsync(lateEarlyBirdResponse);
            Assert.Contains(
                GetDetails(lateEarlyBirdError),
                detail => detail == "EarlyBirdEndsAt must be before the event starts.");

            var foreignResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Foreign",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                otherOrganizer.AccessToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

            await SetStatusAsync(created.Id, EventStatus.PendingApproval);

            var lockedResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Locked",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, lockedResponse.StatusCode);
        }

        [Fact]
        public async Task TicketTypes_UpdateAndDeleteAreGuardByDraftAndReservations()
        {
            var organizer = await RegisterOrganizerAsync("Ticket Mutation Owners");
            var attendee = await RegisterAttendeeAsync();
            var startsAt = DateTimeOffset.UtcNow.AddDays(5);
            var created = await CreateEventAsync(organizer, TestRun.NewTitle("organizer-ticket-mutations"), startsAt);

            var addResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "General",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            var tier = await addResponse.Content.ReadFromJsonAsync<TicketTypeResponse>(
                TestContext.Current.CancellationToken);

            var updateResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}/ticket-types/{tier!.Id}",
                new
                {
                    name = "VIP",
                    price = 200m,
                    quantity = 20,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 150m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await updateResponse.Content.ReadFromJsonAsync<TicketTypeResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("VIP", updated!.Name);
            Assert.Equal(200m, updated.Price);
            Assert.Equal(20, updated.Remaining);

            await TestData.CreateReservationAsync(
                Guid.Parse(attendee.UserId),
                created.Id,
                tier.Id,
                ReservationStatus.Held,
                DateTime.UtcNow.AddMinutes(10),
                cancellationToken: TestContext.Current.CancellationToken);

            var blockedDeleteResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/organizer/events/{created.Id}/ticket-types/{tier.Id}",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, blockedDeleteResponse.StatusCode);

            var secondAddResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "Unused",
                    price = 50m,
                    quantity = 5,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 40m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            var secondTier = await secondAddResponse.Content.ReadFromJsonAsync<TicketTypeResponse>(
                TestContext.Current.CancellationToken);

            var deleteResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/organizer/events/{created.Id}/ticket-types/{secondTier!.Id}",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            var getResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/organizer/events/{created.Id}",
                organizer.AccessToken);
            var eventResponse = await getResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Single(eventResponse!.TicketTypes);
            Assert.Equal(tier.Id, eventResponse.TicketTypes.Single().Id);

            await SetStatusAsync(created.Id, EventStatus.PendingApproval);

            var lockedUpdateResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Put,
                $"/api/v1/organizer/events/{created.Id}/ticket-types/{tier.Id}",
                new
                {
                    name = "Locked",
                    price = 200m,
                    quantity = 20,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 150m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, lockedUpdateResponse.StatusCode);

            var lockedDeleteResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Delete,
                $"/api/v1/organizer/events/{created.Id}/ticket-types/{tier.Id}",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, lockedDeleteResponse.StatusCode);
        }

        [Fact]
        public async Task ImageUpload_ValidatesJpegAndStoresInFakeStorage()
        {
            var organizer = await RegisterOrganizerAsync("Image Owners");
            var created = await CreateEventAsync(organizer, TestRun.NewTitle("organizer-image"));

            var validResponse = await UploadImageAsync(created.Id, organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

            var upload = await validResponse.Content.ReadFromJsonAsync<ImageUploadResponse>(
                TestContext.Current.CancellationToken);
            Assert.NotNull(upload);
            Assert.Equal($"events/{created.Id}/cover.jpg", upload!.ImagePath);
            Assert.Equal($"https://example.invalid/events/{created.Id}/cover.jpg", upload.ImageUrl);
            Assert.True(_images.ContainsKey($"events/{created.Id}/cover.jpg"));

            var eventResponse = await AuthHelper.GetAsync(
                _client,
                $"/api/v1/organizer/events/{created.Id}",
                organizer.AccessToken);
            var eventBody = await eventResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal($"events/{created.Id}/cover.jpg", eventBody!.ImagePath);
            Assert.Equal($"https://example.invalid/events/{created.Id}/cover.jpg", eventBody.ImageUrl);

            var wrongTypeResponse = await UploadImageAsync(
                created.Id,
                organizer.AccessToken,
                contentType: "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, wrongTypeResponse.StatusCode);
            var wrongTypeError = await ReadJsonAsync(wrongTypeResponse);
            Assert.Contains(GetDetails(wrongTypeError), detail => detail == "The image must be a JPEG.");

            var fakeJpegResponse = await UploadImageAsync(
                created.Id,
                organizer.AccessToken,
                new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            Assert.Equal(HttpStatusCode.BadRequest, fakeJpegResponse.StatusCode);
            var fakeJpegError = await ReadJsonAsync(fakeJpegResponse);
            Assert.Contains(
                GetDetails(fakeJpegError),
                detail => detail.Contains("not a real JPEG", StringComparison.OrdinalIgnoreCase));

            var missingFileResponse = await UploadImageAsync(
                created.Id,
                organizer.AccessToken,
                includeFile: false);
            Assert.Equal(HttpStatusCode.BadRequest, missingFileResponse.StatusCode);
            var missingFileError = await ReadJsonAsync(missingFileResponse);
            Assert.Equal("validation_failed", missingFileError.GetProperty("code").GetString());
            Assert.NotEmpty(GetDetails(missingFileError));

            await SetStatusAsync(created.Id, EventStatus.PendingApproval);

            var lockedResponse = await UploadImageAsync(created.Id, organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, lockedResponse.StatusCode);
        }

        [Fact]
        public async Task Submit_ValidatesPrerequisitesAndPublishRequiresApproval()
        {
            var organizer = await RegisterOrganizerAsync("Lifecycle Owners");
            var created = await CreateEventAsync(organizer, TestRun.NewTitle("organizer-submit"));

            var emptySubmit = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/submit",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, emptySubmit.StatusCode);
            var emptyError = await ReadJsonAsync(emptySubmit);
            var emptyDetails = GetDetails(emptyError);
            Assert.Contains(emptyDetails, detail => detail.Contains("cover image", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(emptyDetails, detail => detail.Contains("ticket type", StringComparison.OrdinalIgnoreCase));

            var addTier = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/ticket-types",
                new
                {
                    name = "General",
                    price = 100m,
                    quantity = 10,
                    salesOpenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    earlyBirdPrice = 75m,
                    earlyBirdEndsAt = DateTimeOffset.UtcNow.AddDays(2)
                },
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Created, addTier.StatusCode);

            var noImageSubmit = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/submit",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, noImageSubmit.StatusCode);
            var noImageError = await ReadJsonAsync(noImageSubmit);
            Assert.Contains(
                GetDetails(noImageError),
                detail => detail.Contains("cover image", StringComparison.OrdinalIgnoreCase));

            var imageResponse = await UploadImageAsync(created.Id, organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);

            var submitResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/submit",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
            var submitted = await submitResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("PendingApproval", submitted!.Status);
            Assert.Null(submitted.RejectionReason);

            var publishPendingResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/publish",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, publishPendingResponse.StatusCode);

            await SetStatusAsync(created.Id, EventStatus.Draft);
            await using (var db = TestDatabase.CreateDbContext())
            {
                var eventEntity = await db.Events.SingleAsync(
                    entity => entity.Id == created.Id,
                    TestContext.Current.CancellationToken);
                eventEntity.RejectionReason = "Old rejection reason.";
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var resubmitResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/submit",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);
            var resubmitted = await resubmitResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Null(resubmitted!.RejectionReason);

            await SetStatusAsync(created.Id, EventStatus.Approved);

            var publishResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/publish",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await publishResponse.Content.ReadFromJsonAsync<EventResponse>(
                TestContext.Current.CancellationToken);
            Assert.Equal("Published", published!.Status);

            var doublePublishResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                $"/api/v1/organizer/events/{created.Id}/publish",
                token: organizer.AccessToken);
            Assert.Equal(HttpStatusCode.Conflict, doublePublishResponse.StatusCode);
        }
    }
}
