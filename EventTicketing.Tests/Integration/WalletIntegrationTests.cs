using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Enums;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Integration
{
    public sealed class WalletIntegrationTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _client;

        public WalletIntegrationTests(ApiFixture fixture)
        {
            _client = fixture.Client;
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        private async Task<TestSession> RegisterAttendeeAsync()
        {
            return await AuthHelper.RegisterAttendeeAsync(_client);
        }

        [Fact]
        public async Task Get_EmptyWallet_ReturnsZeroAndCreatesTheWalletRow()
        {
            var attendee = await RegisterAttendeeAsync();

            var response = await AuthHelper.GetAsync(
                _client,
                "/api/v1/wallet",
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var wallet = await response.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.NotNull(wallet);
            Assert.Equal(0m, wallet!.Balance);
            Assert.Empty(wallet.Transactions);

            await using var db = TestDatabase.CreateDbContext();
            var persisted = await db.Wallets.SingleAsync(
                item => item.UserId == Guid.Parse(attendee.UserId),
                TestContext.Current.CancellationToken);
            Assert.Equal(0m, persisted.Balance);
        }

        [Fact]
        public async Task Fund_AddsBalanceAndReturnsLedgerNewestFirst()
        {
            var attendee = await RegisterAttendeeAsync();

            var firstResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 1000m },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var first = await firstResponse.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);

            Assert.NotNull(first);
            Assert.Equal(1000m, first!.Balance);
            var firstTransaction = Assert.Single(first.Transactions);
            Assert.Equal(WalletTransactionType.Fund, firstTransaction.Type);
            Assert.Equal(1000m, firstTransaction.Amount);
            Assert.StartsWith("FUND-", firstTransaction.Reference);
            Assert.Equal(13, firstTransaction.Reference.Length);

            await using (var db = TestDatabase.CreateDbContext())
            {
                var transaction = await db.WalletTransactions.SingleAsync(
                    item => item.Id == firstTransaction.Id,
                    TestContext.Current.CancellationToken);
                transaction.CreatedAt = DateTime.UtcNow.AddHours(-1);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var secondResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 250.50m },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var second = await secondResponse.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Equal(1250.50m, second!.Balance);
            Assert.Equal(2, second.Transactions.Count);

            var latestResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/wallet",
                attendee.AccessToken);
            var latest = await latestResponse.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Equal(1250.50m, latest!.Balance);
            Assert.Equal(2, latest.Transactions.Count);
            Assert.Equal(250.50m, latest.Transactions[0].Amount);
            Assert.Equal(1000m, latest.Transactions[1].Amount);
            Assert.True(latest.Transactions[0].CreatedAt >= latest.Transactions[1].CreatedAt);
            Assert.Equal(1250.50m, latest.Transactions.Sum(item => item.Amount));

            await using var verifyDb = TestDatabase.CreateDbContext();
            var wallet = await verifyDb.Wallets.SingleAsync(
                item => item.UserId == Guid.Parse(attendee.UserId),
                TestContext.Current.CancellationToken);
            Assert.Equal(1250.50m, wallet.Balance);
        }

        [Fact]
        public async Task Fund_ValidatesAmountBounds()
        {
            var attendee = await RegisterAttendeeAsync();

            foreach (var amount in new[] { 0m, -1m, 10000.01m })
            {
                var response = await AuthHelper.SendJsonAsync(
                    _client,
                    HttpMethod.Post,
                    "/api/v1/wallet/fund",
                    new { amount },
                    attendee.AccessToken);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

                var error = await ReadJsonAsync(response);
                Assert.Equal("validation_failed", error.GetProperty("code").GetString());
            }

            var maximumResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 10000m },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.OK, maximumResponse.StatusCode);
            var wallet = await maximumResponse.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Equal(10000m, wallet!.Balance);
        }

        [Fact]
        public async Task Fund_RateLimitIsFivePerUserPerMinute()
        {
            var attendee = await RegisterAttendeeAsync();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await AuthHelper.SendJsonAsync(
                    _client,
                    HttpMethod.Post,
                    "/api/v1/wallet/fund",
                    new { amount = 10m },
                    attendee.AccessToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            var limitedResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 10m },
                attendee.AccessToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);

            var walletResponse = await AuthHelper.GetAsync(
                _client,
                "/api/v1/wallet",
                attendee.AccessToken);
            var wallet = await walletResponse.Content.ReadFromJsonAsync<WalletResponse>(
                JsonOptions,
                TestContext.Current.CancellationToken);
            Assert.Equal(50m, wallet!.Balance);
            Assert.Equal(5, wallet.Transactions.Count);
        }

        [Fact]
        public async Task WalletEndpoints_RequireAuthentication()
        {
            var getResponse = await AuthHelper.GetAsync(_client, "/api/v1/wallet");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

            var fundResponse = await AuthHelper.SendJsonAsync(
                _client,
                HttpMethod.Post,
                "/api/v1/wallet/fund",
                new { amount = 100m });
            Assert.Equal(HttpStatusCode.Unauthorized, fundResponse.StatusCode);
        }
    }
}
