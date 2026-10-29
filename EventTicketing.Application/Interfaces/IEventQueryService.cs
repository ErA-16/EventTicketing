using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;

namespace EventTicketing.Application.Interfaces
{
    public interface IEventQueryService
    {
        Task<CursorPage<EventSummaryResponse>> GetPublishedAsync(CursorQuery query, CancellationToken cancellationToken);

        Task<EventDetailsResponse> GetPublishedByIdAsync(Guid id, CancellationToken cancellationToken);
    }
}
