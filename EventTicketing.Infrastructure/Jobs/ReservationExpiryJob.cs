using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventTicketing.Infrastructure.Jobs
{
    public class ReservationExpiryJob : BackgroundService
    {
        private const int MinIntervalSeconds = 5;
        private const int MaxBatchSize = 1000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReservationExpiryJob> _logger;
        private readonly TimeSpan _interval;
        private readonly int _batchSize;

        public ReservationExpiryJob(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ReservationExpiryJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            var intervalSeconds = configuration.GetValue<int?>("ReservationExpiry:IntervalSeconds") ?? 60;
            if (intervalSeconds < MinIntervalSeconds)
            {
                throw new InvalidOperationException(
                    $"ReservationExpiry:IntervalSeconds must be at least {MinIntervalSeconds}.");
            }

            _batchSize = configuration.GetValue<int?>("ReservationExpiry:BatchSize") ?? 100;
            if (_batchSize < 1 || _batchSize > MaxBatchSize)
            {
                throw new InvalidOperationException(
                    $"ReservationExpiry:BatchSize must be between 1 and {MaxBatchSize}.");
            }

            _interval = TimeSpan.FromSeconds(intervalSeconds);
        }

        private async Task SweepAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var expired = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH due AS (
                        SELECT "Id"
                        FROM "Reservations"
                        WHERE "Status" = 'Held'
                          AND "ExpiresAt" <= now()
                        ORDER BY "ExpiresAt"
                        LIMIT {_batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE "Reservations" AS r
                    SET "Status" = 'Expired'
                    FROM due
                    WHERE r."Id" = due."Id";
                    """, cancellationToken).ConfigureAwait(false);

                if (expired > 0)
                {
                    _logger.LogInformation("Expired {Count} stale reservation(s).", expired);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reservation expiry sweep failed.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await SweepAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await SweepAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
