using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventTicketing.Infrastructure.Jobs
{
    public class DataRetentionJob : BackgroundService
    {
        private const int MinIntervalMinutes = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DataRetentionJob> _logger;
        private readonly TimeSpan _interval;

        public DataRetentionJob(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<DataRetentionJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            var intervalMinutes = configuration.GetValue<int?>("DataRetention:IntervalMinutes") ?? 60;
            if (intervalMinutes < MinIntervalMinutes)
            {
                throw new InvalidOperationException(
                    $"DataRetention:IntervalMinutes must be at least {MinIntervalMinutes}.");
            }

            _interval = TimeSpan.FromMinutes(intervalMinutes);
        }

        private async Task SweepAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDataRetentionService>();
                await service.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention sweep failed.");
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
