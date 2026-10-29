using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Interfaces;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EventTicketing.Infrastructure.Services
{
    public class DataRetentionService : IDataRetentionService
    {
        private const int MaxBatchSize = 1000;
        private const int MaxBatchesPerSweep = 100;

        private readonly AppDbContext _db;
        private readonly ILogger<DataRetentionService> _logger;
        private readonly int _batchSize;
        private readonly int _maxBatchesPerSweep;
        private readonly int _deadReservationDaysAfterEvent;
        private readonly int _refreshTokenDaysAfterExpiry;

        public DataRetentionService(
            AppDbContext db,
            IConfiguration configuration,
            ILogger<DataRetentionService> logger)
        {
            _db = db;
            _logger = logger;

            _batchSize = configuration.GetValue<int?>("DataRetention:BatchSize") ?? 500;
            if (_batchSize < 1 || _batchSize > MaxBatchSize)
            {
                throw new InvalidOperationException(
                    $"DataRetention:BatchSize must be between 1 and {MaxBatchSize}.");
            }

            _maxBatchesPerSweep = configuration.GetValue<int?>("DataRetention:MaxBatchesPerSweep") ?? 20;
            if (_maxBatchesPerSweep < 1 || _maxBatchesPerSweep > MaxBatchesPerSweep)
            {
                throw new InvalidOperationException(
                    $"DataRetention:MaxBatchesPerSweep must be between 1 and {MaxBatchesPerSweep}.");
            }

            _deadReservationDaysAfterEvent = configuration.GetValue<int?>("DataRetention:DeadReservationDaysAfterEvent") ?? 30;
            if (_deadReservationDaysAfterEvent < 1)
            {
                throw new InvalidOperationException(
                    "DataRetention:DeadReservationDaysAfterEvent must be at least 1.");
            }

            _refreshTokenDaysAfterExpiry = configuration.GetValue<int?>("DataRetention:RefreshTokenDaysAfterExpiry") ?? 7;
            if (_refreshTokenDaysAfterExpiry < 1)
            {
                throw new InvalidOperationException(
                    "DataRetention:RefreshTokenDaysAfterExpiry must be at least 1.");
            }
        }

        private async Task<int> RunBatchesAsync(
            Func<CancellationToken, Task<int>> batch,
            CancellationToken cancellationToken)
        {
            var total = 0;

            for (var index = 0; index < _maxBatchesPerSweep; index++)
            {
                var affected = await batch(cancellationToken).ConfigureAwait(false);
                total += affected;

                if (affected < _batchSize)
                {
                    break;
                }
            }

            return total;
        }

        private Task<int> PurgeDeadReservationsAsync(CancellationToken cancellationToken)
        {
            return RunBatchesAsync(
                ct => _db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH due AS (
                        SELECT r."Id"
                        FROM "Reservations" AS r
                        INNER JOIN "Events" AS e ON e."Id" = r."EventId"
                        WHERE r."Status" IN ('Expired', 'Cancelled')
                          AND e."StartsAt" < now() - ({_deadReservationDaysAfterEvent} * interval '1 day')
                          AND NOT EXISTS (
                              SELECT 1
                              FROM "Tickets" AS t
                              WHERE t."ReservationId" = r."Id"
                          )
                        ORDER BY r."ExpiresAt"
                        LIMIT {_batchSize}
                        FOR UPDATE OF r SKIP LOCKED
                    )
                    DELETE FROM "Reservations" AS r
                    USING due
                    WHERE r."Id" = due."Id";
                    """, ct),
                cancellationToken);
        }

        private Task<int> ClearIdempotencyKeysAsync(CancellationToken cancellationToken)
        {
            var retentionHours = (int)ReservationService.IdempotencyRetention.TotalHours;

            return RunBatchesAsync(
                ct => _db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH due AS (
                        SELECT "Id"
                        FROM "Reservations"
                        WHERE "Status" = 'Paid'
                          AND "PaymentIdempotencyKey" IS NOT NULL
                          AND "PaidAt" IS NOT NULL
                          AND "PaidAt" < now() - ({retentionHours} * interval '1 hour')
                        ORDER BY "PaidAt"
                        LIMIT {_batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE "Reservations" AS r
                    SET "PaymentIdempotencyKey" = NULL
                    FROM due
                    WHERE r."Id" = due."Id";
                    """, ct),
                cancellationToken);
        }

        private Task<int> PurgeRefreshTokensAsync(CancellationToken cancellationToken)
        {
            return RunBatchesAsync(
                ct => _db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH due AS (
                        SELECT "Id"
                        FROM "RefreshTokens"
                        WHERE "ExpiresAt" < now() - ({_refreshTokenDaysAfterExpiry} * interval '1 day')
                           OR "RevokedAt" < now() - ({_refreshTokenDaysAfterExpiry} * interval '1 day')
                           OR "AbsoluteExpiresAt" < now() - ({_refreshTokenDaysAfterExpiry} * interval '1 day')
                        ORDER BY "ExpiresAt"
                        LIMIT {_batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    DELETE FROM "RefreshTokens" AS r
                    USING due
                    WHERE r."Id" = due."Id";
                    """, ct),
                cancellationToken);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var reservations = await PurgeDeadReservationsAsync(cancellationToken).ConfigureAwait(false);
            var idempotencyKeys = await ClearIdempotencyKeysAsync(cancellationToken).ConfigureAwait(false);
            var refreshTokens = await PurgeRefreshTokensAsync(cancellationToken).ConfigureAwait(false);

            if (reservations > 0 || idempotencyKeys > 0 || refreshTokens > 0)
            {
                _logger.LogInformation(
                    "Retention sweep deleted {Reservations} dead reservation(s), cleared {IdempotencyKeys} idempotency key(s), and deleted {RefreshTokens} refresh token(s).",
                    reservations,
                    idempotencyKeys,
                    refreshTokens);
            }
        }
    }
}
