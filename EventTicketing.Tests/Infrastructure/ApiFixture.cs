using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace EventTicketing.Tests.Infrastructure
{
    public sealed class ApiFixture : IAsyncDisposable
    {
        public ApiFactory Factory { get; } = new();

        public HttpClient Client { get; }

        public ApiFixture()
        {
            Client = Factory.CreateClient();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            await TestDatabase.CleanupAsync(TestRun.Prefix, TestRun.AdminEmail);
        }
    }
}
