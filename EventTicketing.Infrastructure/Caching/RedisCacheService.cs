using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EventTicketing.Infrastructure.Caching
{
    public class RedisCacheService : ICacheService
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly RedisOptions _options;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly IDatabase _db;

        public RedisCacheService(IConnectionMultiplexer mux, IOptions<RedisOptions> options, ILogger<RedisCacheService> logger)
        {
            _mux = mux;
            _options = options.Value;
            _logger = logger;
            _db = _mux.GetDatabase();
        }

        private string BuildKey(string key)
        {
            if (string.IsNullOrEmpty(_options.InstanceName))
            {
                return key;
            }

            return _options.InstanceName + key;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var redisKey = BuildKey(key);
                var value = await _db.StringGetAsync(redisKey).ConfigureAwait(false);
                if (!value.HasValue)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis GetAsync failed for key {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            try
            {
                var redisKey = BuildKey(key);
                var json = JsonSerializer.Serialize(value);
                var expiry = TimeSpan.FromSeconds(_options.TtlSeconds > 0 ? _options.TtlSeconds : 120);
                await _db.StringSetAsync(redisKey, json, expiry).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis SetAsync failed for key {Key}", key);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var redisKey = BuildKey(key);
                await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis RemoveAsync failed for key {Key}", key);
            }
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            try
            {
                var pattern = BuildKey(prefix) + "*";
                foreach (var server in _mux.GetServers())
                {
                    await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: 250).WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        await _db.KeyDeleteAsync(key).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis RemoveByPrefixAsync failed for prefix {Prefix}", prefix);
            }
        }
    }
}
