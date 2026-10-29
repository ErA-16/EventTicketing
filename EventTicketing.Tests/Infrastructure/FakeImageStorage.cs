using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Interfaces;

namespace EventTicketing.Tests.Infrastructure
{
    public sealed class FakeImageStorage : IImageStorage
    {
        private readonly ConcurrentDictionary<string, FakeImage> _images = new();

        public IReadOnlyList<string> Keys => _images.Keys.OrderBy(key => key).ToList();

        public async Task<string> SaveAsync(
            string key,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);

            _images[key] = new FakeImage(contentType, buffer.ToArray());
            return key;
        }

        public FakeImage? Get(string key)
        {
            return _images.TryGetValue(key, out var image) ? image : null;
        }

        public bool ContainsKey(string key)
        {
            return _images.ContainsKey(key);
        }
    }

    public sealed record FakeImage(string ContentType, byte[] Content);
}
