using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IDataRetentionService
    {
        Task RunAsync(CancellationToken cancellationToken);
    }
}
