using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class AuthIntegrationTests
    {
        private const string RoleClaim = "role";
        private const string MappedRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

        private readonly HttpClient _client;

        public AuthIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        private static IReadOnlyList<string> GetRoles(JsonElement payload)
        {
            var roles = new List<string>();

            foreach (var claimName in new[] { RoleClaim, MappedRoleClaim })
            {
                if (!payload.TryGetProperty(claimName, out var claim))
                {
                    continue;
                }

                if (claim.ValueKind == JsonValueKind.Array)
                {
                    roles.AddRange(claim.EnumerateArray()
                        .Select(value => value.GetString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))!);
                }
                else if (claim.ValueKind == JsonValueKind.String)
                {
                    var value = claim.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        roles.Add(value);
                    }
                }
            }

            return roles;
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

        [Fact]
        public async Task Register_ValidAttendee_CreatesUserWalletAndRole()
        {
            var email = TestRun.NewEmail("auth-attendee");

            var session = await AuthHelper.RegisterAttendeeAsync(_client, email);

            Assert.Equal(email, session.Email);
            Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
            Assert.Matches("^[0-9A-F]{64}$", session.RefreshToken);

            using var payload = JwtTestHelper.ReadPayload(session.AccessToken);
            Assert.Equal(session.UserId, payload.RootElement.GetProperty("sub").GetString());
            Assert.Equal(email, payload.RootElement.GetProperty("email").GetString());
            Assert.Equal(
                300,
                payload.RootElement.GetProperty("exp").GetInt64()
                    - payload.RootElement.GetProperty("nbf").GetInt64());
            Assert.Contains("Attendee", GetRoles(payload.RootElement));

            await using var db = TestDatabase.CreateDbContext();
            var user = await db.Users
                .Include(user => user.Wallet)
                .SingleAsync(user => user.Email == email, TestContext.Current.CancellationToken);

            Assert.Equal(session.UserId, user.Id.ToString());
            Assert.Equal("Test", user.FirstName);
            Assert.Equal("User", user.LastName);
            Assert.NotNull(user.Wallet);
            Assert.Equal(0m, user.Wallet!.Balance);

            var roleNames = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id
                select role.Name)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Contains("Attendee", roleNames);
        }

        [Fact]
        public async Task Register_DuplicateEmail_ReturnsRegistrationFailure()
        {
            var email = TestRun.NewEmail("auth-duplicate");
            await AuthHelper.RegisterAttendeeAsync(_client, email);

            var response = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/register",
                new
                {
                    firstName = "Test",
                    lastName = "User",
                    organizationName = (string?)null,
                    email,
                    password = TestRun.Password,
                    isOrganizer = false
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await ReadJsonAsync(response);
            Assert.Equal("registration_failed", error.GetProperty("code").GetString());
            Assert.Contains(
                GetDetails(error),
                detail => detail == "An account with this email already exists.");
        }

        [Fact]
        public async Task Register_InvalidPasswords_ReturnsValidationDetails()
        {
            var shortPasswordEmail = TestRun.NewEmail("auth-short-password");
            var shortPasswordResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/register",
                new
                {
                    firstName = "Test",
                    lastName = "User",
                    organizationName = (string?)null,
                    email = shortPasswordEmail,
                    password = "weak",
                    isOrganizer = false
                });

            Assert.Equal(HttpStatusCode.BadRequest, shortPasswordResponse.StatusCode);
            var shortPasswordError = await ReadJsonAsync(shortPasswordResponse);
            Assert.Equal("validation_failed", shortPasswordError.GetProperty("code").GetString());

            var weakPasswordEmail = TestRun.NewEmail("auth-weak-password");
            var weakPasswordResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/register",
                new
                {
                    firstName = "Test",
                    lastName = "User",
                    organizationName = (string?)null,
                    email = weakPasswordEmail,
                    password = "lowercase1!",
                    isOrganizer = false
                });

            Assert.Equal(HttpStatusCode.BadRequest, weakPasswordResponse.StatusCode);
            var weakPasswordError = await ReadJsonAsync(weakPasswordResponse);
            Assert.Equal("registration_failed", weakPasswordError.GetProperty("code").GetString());
            Assert.Contains(
                GetDetails(weakPasswordError),
                detail => detail.Contains("uppercase", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Login_SeededAdmin_ReturnsAdminToken()
        {
            var session = await AuthHelper.CreateAdminAsync(_client);

            Assert.Equal(TestRun.AdminEmail, session.Email);
            Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));

            using var payload = JwtTestHelper.ReadPayload(session.AccessToken);
            Assert.Equal(TestRun.AdminEmail, payload.RootElement.GetProperty("email").GetString());
            Assert.Contains("Admin", GetRoles(payload.RootElement));
        }

        [Fact]
        public async Task Login_UnknownEmail_ReturnsGenericFailure()
        {
            var response = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/login",
                new
                {
                    email = TestRun.NewEmail("auth-unknown"),
                    password = TestRun.Password
                });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var error = await ReadJsonAsync(response);
            Assert.Equal("login_failed", error.GetProperty("code").GetString());
            Assert.Contains(
                GetDetails(error),
                detail => detail == "Invalid email or password.");
        }

        [Fact]
        public async Task Register_Organizer_CreatesOrganizationAccount()
        {
            var email = TestRun.NewEmail("auth-organizer");
            var session = await AuthHelper.RegisterOrganizerAsync(
                _client,
                email,
                organizationName: "Test Events Ltd");

            using var payload = JwtTestHelper.ReadPayload(session.AccessToken);
            Assert.Contains("Organizer", GetRoles(payload.RootElement));

            await using var db = TestDatabase.CreateDbContext();
            var user = await db.Users
                .Include(user => user.Wallet)
                .SingleAsync(
                    user => user.Email == email,
                    TestContext.Current.CancellationToken);

            Assert.Equal("Test Events Ltd", user.OrganizationName);
            Assert.Equal(string.Empty, user.FirstName);
            Assert.Equal(string.Empty, user.LastName);
            Assert.NotNull(user.Wallet);

            var roleNames = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id
                select role.Name)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Contains("Organizer", roleNames);
        }

        [Fact]
        public async Task Refresh_ValidToken_ExtendsSameStoredToken()
        {
            var session = await AuthHelper.RegisterAttendeeAsync(_client, TestRun.NewEmail("auth-refresh"));
            var userId = Guid.Parse(session.UserId);
            var cancellationToken = TestContext.Current.CancellationToken;

            await using var db = TestDatabase.CreateDbContext();
            var stored = await db.RefreshTokens
                .SingleAsync(token => token.UserId == userId, cancellationToken);

            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync(cancellationToken);

            var response = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/refresh",
                new { refreshToken = session.RefreshToken });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await ReadJsonAsync(response);
            var newToken = body.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(newToken));
            Assert.Equal(300, body.GetProperty("expiresIn").GetInt32());
            Assert.False(body.TryGetProperty("refreshToken", out _));

            var refreshed = await db.RefreshTokens
                .AsNoTracking()
                .SingleAsync(token => token.UserId == userId, cancellationToken);

            Assert.Equal(stored.Id, refreshed.Id);
            Assert.True(refreshed.ExpiresAt > stored.ExpiresAt);
            Assert.Null(refreshed.RevokedAt);

            var tokenCount = await db.RefreshTokens
                .CountAsync(token => token.UserId == userId, cancellationToken);
            Assert.Equal(1, tokenCount);
        }

        [Fact]
        public async Task Logout_RevokesToken_AndIsIdempotent()
        {
            var session = await AuthHelper.RegisterAttendeeAsync(_client, TestRun.NewEmail("auth-logout"));

            var logoutResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/logout",
                new { refreshToken = session.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

            var refreshResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/refresh",
                new { refreshToken = session.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

            var refreshError = await ReadJsonAsync(refreshResponse);
            Assert.Equal("refresh_failed", refreshError.GetProperty("code").GetString());

            var secondLogout = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/logout",
                new { refreshToken = session.RefreshToken });
            Assert.Equal(HttpStatusCode.NoContent, secondLogout.StatusCode);
        }

        [Fact]
        public async Task Refresh_ExpiredToken_ReturnsRefreshFailure()
        {
            var session = await AuthHelper.RegisterAttendeeAsync(_client, TestRun.NewEmail("auth-expired-refresh"));
            var userId = Guid.Parse(session.UserId);
            var cancellationToken = TestContext.Current.CancellationToken;

            await using var db = TestDatabase.CreateDbContext();
            var stored = await db.RefreshTokens
                .SingleAsync(token => token.UserId == userId, cancellationToken);

            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(cancellationToken);

            var response = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/refresh",
                new { refreshToken = session.RefreshToken });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            var error = await ReadJsonAsync(response);
            Assert.Equal("refresh_failed", error.GetProperty("code").GetString());
            Assert.Equal(
                "Session expired. Please log in again.",
                error.GetProperty("message").GetString());
        }

        [Fact]
        public async Task Login_FiveWrongPasswords_LocksAccount()
        {
            var email = TestRun.NewEmail("auth-lockout");
            await AuthHelper.RegisterAttendeeAsync(_client, email);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var wrongPassword = await AuthHelper.PostJsonAsync(
                    _client,
                    "/api/v1/auth/login",
                    new { email, password = "WrongPass123!" });

                Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            }

            var lockedResponse = await AuthHelper.PostJsonAsync(
                _client,
                "/api/v1/auth/login",
                new { email, password = TestRun.Password });

            Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);

            var error = await ReadJsonAsync(lockedResponse);
            Assert.Equal("login_failed", error.GetProperty("code").GetString());
            Assert.Contains(
                GetDetails(error),
                detail => detail.Contains("locked out", StringComparison.OrdinalIgnoreCase));
        }
    }
}
