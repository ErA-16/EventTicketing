using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace EventTicketing.Tests.Infrastructure
{
    public static class TestDatabase
    {
        private static readonly Lazy<string> ConnectionStringValue = new(LoadConnectionString);

        public static string ConnectionString => ConnectionStringValue.Value;

        private static string LoadConnectionString()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is required for the database tests.");
            }

            var expectedDatabase = Environment.GetEnvironmentVariable("EVENTTICKETING_TEST_DB_NAME")
                ?? "neondb";
            var database = new NpgsqlConnectionStringBuilder(connectionString).Database;

            if (!string.Equals(database, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to run tests against database '{database}'. Expected '{expectedDatabase}'.");
            }

            return connectionString;
        }

        public static AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            return new AppDbContext(options);
        }

        public static async Task CleanupAsync(string prefix, string? adminEmail = null)
        {
            await using var db = CreateDbContext();

            var userIds = await db.Users
                .Where(user => user.Email != null
                    && (user.Email.StartsWith(prefix) || user.Email == adminEmail))
                .Select(user => user.Id)
                .ToListAsync();

            var eventIds = await db.Events
                .Where(eventEntity => eventEntity.Title.StartsWith(prefix))
                .Select(eventEntity => eventEntity.Id)
                .ToListAsync();

            if (userIds.Count == 0 && eventIds.Count == 0)
            {
                return;
            }

            await db.Tickets
                .Where(ticket => userIds.Contains(ticket.UserId) || eventIds.Contains(ticket.EventId))
                .ExecuteDeleteAsync();

            await db.Reservations
                .Where(reservation => userIds.Contains(reservation.UserId) || eventIds.Contains(reservation.EventId))
                .ExecuteDeleteAsync();

            await db.TicketTypes
                .Where(ticketType => eventIds.Contains(ticketType.EventId))
                .ExecuteDeleteAsync();

            await db.Events
                .Where(eventEntity => eventIds.Contains(eventEntity.Id))
                .ExecuteDeleteAsync();

            await db.WalletTransactions
                .Where(transaction => userIds.Contains(transaction.UserId))
                .ExecuteDeleteAsync();

            await db.Wallets
                .Where(wallet => userIds.Contains(wallet.UserId))
                .ExecuteDeleteAsync();

            await db.RefreshTokens
                .Where(token => userIds.Contains(token.UserId))
                .ExecuteDeleteAsync();

            await db.Users
                .Where(user => userIds.Contains(user.Id))
                .ExecuteDeleteAsync();
        }
    }
}
