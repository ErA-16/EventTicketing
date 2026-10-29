using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IAdminOrganizerService
    {
        Task<CursorPage<AdminOrganizerResponse>> GetOrganizersAsync(AdminOrganizerQuery query, CancellationToken cancellationToken = default);
        Task SuspendAsync(Guid organizerId, CancellationToken cancellationToken = default);
        Task UnsuspendAsync(Guid organizerId, CancellationToken cancellationToken = default);
    }
}
