using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Interfaces;

namespace EventTicketing.Tests.Infrastructure
{
    public sealed class FakeCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, object?> _values = new();

        public IReadOnlyList<string> Keys => _values.Keys.OrderBy(key => key).ToList();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in _values.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _values.TryRemove(key, out _);
                }
            }

            return Task.CompletedTask;
        }

        public bool ContainsKey(string key)
        {
            return _values.ContainsKey(key);
        }

        public void Clear()
        {
            _values.Clear();
        }
    }
}
