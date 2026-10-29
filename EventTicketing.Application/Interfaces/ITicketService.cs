using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;

namespace EventTicketing.Application.Interfaces
{
    public interface ITicketService
    {
        Task<CheckInTicketResponse> CheckInAsync(Guid organizerId, CheckInTicketRequest request, CancellationToken cancellationToken);

        Task<CursorPage<MyTicketResponse>> GetMyTicketsAsync(Guid attendeeId, string? cursor, int limit, CancellationToken cancellationToken);
    }
}
