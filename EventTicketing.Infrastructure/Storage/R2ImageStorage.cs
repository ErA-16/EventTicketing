using Amazon.S3;
using Amazon.S3.Model;
using EventTicketing.Application.Interfaces;
using Microsoft.Extensions.Options;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Infrastructure.Storage
{
    public class R2ImageStorage : IImageStorage
    {
        private readonly IAmazonS3 _s3;
        private readonly R2Options _options;

        public R2ImageStorage(IAmazonS3 s3, IOptions<R2Options> options)
        {
            _s3 = s3;
            _options = options.Value;
        }

        public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                // R2 rejects AWS chunked (STREAMING-AWS4-HMAC-SHA256-PAYLOAD) bodies.
                DisablePayloadSigning = true,
                UseChunkEncoding = false
            };

            await _s3.PutObjectAsync(request, cancellationToken);

            // The database stores the key only; the public URL is built from
            // R2:PublicBaseUrl at read time, so swapping domains is a config change.
            return key;
        }
    }
}
