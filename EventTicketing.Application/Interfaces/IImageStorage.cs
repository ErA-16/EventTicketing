using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IImageStorage
    {
        Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    }
}
