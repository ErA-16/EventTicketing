using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IAdminUserService
    {
        Task<CursorPage<AdminUserResponse>> GetUsersAsync(AdminUserQuery query, CancellationToken cancellationToken = default);
    }
}
