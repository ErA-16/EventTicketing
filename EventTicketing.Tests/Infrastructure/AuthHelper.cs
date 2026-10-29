using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;

namespace EventTicketing.Tests.Infrastructure
{
    public static class AuthHelper
    {
        public static async Task<TestSession> RegisterAttendeeAsync(
            HttpClient client,
            string? email = null,
            string? password = null,
            string firstName = "Test",
            string lastName = "User")
        {
            email ??= TestRun.NewEmail("attendee");
            password ??= TestRun.Password;

            var response = await PostJsonAsync(
                client,
                "/api/v1/auth/register",
                new
                {
                    firstName,
                    lastName,
                    organizationName = (string?)null,
                    email,
                    password,
                    isOrganizer = false
                });

            await EnsureStatusAsync(response, HttpStatusCode.Created);
            return await LoginAsync(client, email, password);
        }

        public static async Task<TestSession> RegisterOrganizerAsync(
            HttpClient client,
            string? email = null,
            string? password = null,
            string organizationName = "Test Organization")
        {
            email ??= TestRun.NewEmail("organizer");
            password ??= TestRun.Password;

            var response = await PostJsonAsync(
                client,
                "/api/v1/auth/register",
                new
                {
                    firstName = (string?)null,
                    lastName = (string?)null,
                    organizationName,
                    email,
                    password,
                    isOrganizer = true
                });

            await EnsureStatusAsync(response, HttpStatusCode.Created);
            return await LoginAsync(client, email, password);
        }

        public static async Task<TestSession> LoginAsync(
            HttpClient client,
            string email,
            string password)
        {
            var response = await PostJsonAsync(
                client,
                "/api/v1/auth/login",
                new { email, password });

            await EnsureStatusAsync(response, HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<AuthResponse>()
                ?? throw new InvalidOperationException("Login returned no JSON body.");

            return new TestSession(
                email,
                password,
                body.Token ?? throw new InvalidOperationException("Login returned no access token."),
                body.RefreshToken ?? throw new InvalidOperationException("Login returned no refresh token."),
                JwtTestHelper.GetUserId(body.Token));
        }

        public static Task<TestSession> CreateAdminAsync(HttpClient client)
        {
            return LoginAsync(client, TestRun.AdminEmail, ApiFactory.AdminPassword);
        }

        public static Task<HttpResponseMessage> PostJsonAsync(
            HttpClient client,
            string path,
            object body)
        {
            return SendJsonAsync(client, HttpMethod.Post, path, body);
        }

        public static async Task<HttpResponseMessage> SendJsonAsync(
            HttpClient client,
            HttpMethod method,
            string path,
            object? body = null,
            string? token = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Forwarded-For", TestRun.NextIp());

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await client.SendAsync(request);
        }

        public static async Task<HttpResponseMessage> GetAsync(
            HttpClient client,
            string path,
            string? token = null)
        {
            return await SendJsonAsync(client, HttpMethod.Get, path, token: token);
        }

        private static async Task EnsureStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            if (response.StatusCode == expected)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Expected {(int)expected} but got {(int)response.StatusCode}. Body: {body}");
        }
    }

    public sealed record TestSession(
        string Email,
        string Password,
        string AccessToken,
        string RefreshToken,
        string UserId);
}
