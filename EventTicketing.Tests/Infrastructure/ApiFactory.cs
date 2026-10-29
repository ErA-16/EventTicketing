using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using EventTicketing.Application.Interfaces;
using EventTicketing.Infrastructure.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EventTicketing.Tests.Infrastructure
{
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public const string AdminPassword = "TestAdmin123!";

        public const string JwtKey = "test-only-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz";

        public FakeCacheService Cache { get; } = new();

        public FakeImageStorage Images { get; } = new();

        public ApiFactory()
        {
            SetEnvironment("ConnectionStrings__DefaultConnection", TestDatabase.ConnectionString);
            SetEnvironment("Jwt__Key", JwtKey);
            SetEnvironment("Jwt__Issuer", "EventTicketing.Tests");
            SetEnvironment("Jwt__Audience", "EventTicketing.Tests");
            SetEnvironment("Jwt__ExpiryMinutes", "5");
            SetEnvironment("RefreshToken__IdleMinutes", "30");
            SetEnvironment("Seed__AdminEmail", TestRun.AdminEmail);
            SetEnvironment("Seed__AdminPassword", AdminPassword);
            SetEnvironment("Seed__DemoOrganizerEmail", " ");
            SetEnvironment("Seed__DemoOrganizerPassword", " ");
            SetEnvironment("Redis__Configuration", "127.0.0.1:6379,abortConnect=false");
            SetEnvironment("R2__Endpoint", "https://example.invalid");
            SetEnvironment("R2__AccessKeyId", "test");
            SetEnvironment("R2__SecretAccessKey", "test");
            SetEnvironment("R2__BucketName", "test");
            SetEnvironment("R2__PublicBaseUrl", "https://example.invalid");
            SetEnvironment("R2__MaxImageBytes", "2621440");
            SetEnvironment("ReservationExpiry__IntervalSeconds", "3600");
            SetEnvironment("ReservationExpiry__BatchSize", "100");
            SetEnvironment("Logging__LogLevel__Default", "Warning");
            SetEnvironment("Logging__LogLevel__GlobalExceptionHandler", "Critical");
            SetEnvironment("Logging__LogLevel__Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", "Critical");
            SetEnvironment("Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command", "Warning");
            SetEnvironment("Logging__LogLevel__Microsoft.EntityFrameworkCore.Query", "Error");
        }

        private static void SetEnvironment(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICacheService>();
                services.AddSingleton<ICacheService>(Cache);

                services.RemoveAll<IImageStorage>();
                services.AddSingleton<IImageStorage>(Images);

                var expiryJob = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType == typeof(ReservationExpiryJob))
                    .ToList();

                foreach (var descriptor in expiryJob)
                {
                    services.Remove(descriptor);
                }
            });
        }

        public HttpClient CreateAuthenticatedClient(string token)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("X-Forwarded-For", TestRun.NextIp());
            return client;
        }
    }
}
