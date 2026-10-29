using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EventTicketing.Tests.Infrastructure;

namespace EventTicketing.Tests.Security
{
    public sealed class SecurityIntegrationTests
    {
        private readonly HttpClient _client;

        public SecurityIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private async Task<HttpResponseMessage> SendRegisterAsync(
            string ipAddress,
            string email)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/register");
            request.Headers.Add("X-Forwarded-For", ipAddress);
            request.Content = JsonContent.Create(new
            {
                firstName = "Rate",
                lastName = "Limit",
                organizationName = (string?)null,
                email,
                password = TestRun.Password,
                isOrganizer = false
            });
            return await _client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
        }

        private async Task<HttpResponseMessage> SendLoginAsync(
            string ipAddress,
            string email)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/login");
            request.Headers.Add("X-Forwarded-For", ipAddress);
            request.Content = JsonContent.Create(new
            {
                email,
                password = "WrongPassword123!"
            });
            return await _client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task ProtectedEndpoints_RejectMissingMalformedAndTamperedTokens()
        {
            var missingTokenResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events");
            Assert.Equal(HttpStatusCode.Unauthorized, missingTokenResponse.StatusCode);

            var malformedResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                "not-a-jwt");
            Assert.Equal(HttpStatusCode.Unauthorized, malformedResponse.StatusCode);

            var organizer = await AuthHelper.RegisterOrganizerAsync(_client);
            var parts = organizer.AccessToken.Split('.');
            var signature = parts[2];
            parts[2] = (signature[0] == 'a' ? "b" : "a") + signature[1..];
            var tamperedToken = string.Join('.', parts);

            var tamperedResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                tamperedToken);
            Assert.Equal(HttpStatusCode.Unauthorized, tamperedResponse.StatusCode);

            var validResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                organizer.AccessToken);
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

            var attendee = await AuthHelper.RegisterAttendeeAsync(_client);
            var attendeeResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/organizer/events",
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, attendeeResponse.StatusCode);
        }

        [Fact]
        public async Task ModelValidationAndUnknownRoutes_ReturnStableShapes()
        {
            var validationResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/auth/register",
                new
                {
                    firstName = string.Empty,
                    lastName = string.Empty,
                    organizationName = (string?)null,
                    email = "not-an-email",
                    password = "weak",
                    isOrganizer = false
                });
            Assert.Equal(HttpStatusCode.BadRequest, validationResponse.StatusCode);
            var validationBody = await validationResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);
            using var validationJson = JsonDocument.Parse(validationBody);
            Assert.Equal(
                "validation_failed",
                validationJson.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                validationJson.RootElement.GetProperty("message").GetString()));
            Assert.True(
                validationJson.RootElement.GetProperty("details").GetArrayLength() > 0);
            Assert.DoesNotContain(" at EventTicketing.", validationBody);

            var unsupportedVersionResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v2/events");
            Assert.Equal(
                HttpStatusCode.NotFound,
                unsupportedVersionResponse.StatusCode);

            var unknownRouteResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/does-not-exist");
            Assert.Equal(
                HttpStatusCode.NotFound,
                unknownRouteResponse.StatusCode);
        }

        [Fact]
        public async Task AuthRateLimits_AreEnforcedPerIp()
        {
            var registerIp = TestRun.NextIp();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var response = await SendRegisterAsync(
                    registerIp,
                    TestRun.NewEmail($"register-rate-{attempt}"));
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            var limitedRegisterResponse = await SendRegisterAsync(
                registerIp,
                TestRun.NewEmail("register-rate-limited"));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                limitedRegisterResponse.StatusCode);

            var loginIp = TestRun.NextIp();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await SendLoginAsync(
                    loginIp,
                    TestRun.NewEmail($"login-rate-{attempt}"));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            var limitedLoginResponse = await SendLoginAsync(
                loginIp,
                TestRun.NewEmail("login-rate-limited"));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                limitedLoginResponse.StatusCode);
        }

        [Fact]
        public async Task Cors_AllowsTheConfiguredFrontendOriginOnly()
        {
            using var allowedRequest = new HttpRequestMessage(
                HttpMethod.Options,
                "/api/v1/events");
            allowedRequest.Headers.Add("Origin", "http://localhost:5173");
            allowedRequest.Headers.Add("Access-Control-Request-Method", "GET");
            var allowedResponse = await _client.SendAsync(
                allowedRequest,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
            Assert.Equal(
                "http://localhost:5173",
                allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

            using var deniedRequest = new HttpRequestMessage(
                HttpMethod.Options,
                "/api/v1/events");
            deniedRequest.Headers.Add("Origin", "https://evil.example");
            deniedRequest.Headers.Add("Access-Control-Request-Method", "GET");
            var deniedResponse = await _client.SendAsync(
                deniedRequest,
                TestContext.Current.CancellationToken);

            Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }
}
