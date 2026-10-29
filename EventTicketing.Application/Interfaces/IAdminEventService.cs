using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IAdminEventService
    {
        Task<CursorPage<EventResponse>> GetQueueAsync(CursorQuery query, CancellationToken cancellationToken = default);
        Task<EventResponse> ApproveAsync(Guid eventId, CancellationToken cancellationToken = default);
        Task<EventResponse> RejectAsync(Guid eventId, RejectEventRequest request, CancellationToken cancellationToken = default);
        Task<EventResponse> SuspendAsync(Guid eventId, CancellationToken cancellationToken = default);
        Task<EventResponse> UnsuspendAsync(Guid eventId, CancellationToken cancellationToken = default);
    }
}
