using Asp.Versioning;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Authorize(Roles = "Admin")]
    [Route("api/v{version:apiVersion}/admin/users")]
    public class AdminUsersController : ControllerBase
    {
        private readonly IAdminUserService _adminUserService;

        public AdminUsersController(IAdminUserService adminUserService)
        {
            _adminUserService = adminUserService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(CursorPage<AdminUserResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUsers([FromQuery] AdminUserQuery query, CancellationToken cancellationToken)
        {
            var result = await _adminUserService.GetUsersAsync(query, cancellationToken);
            return Ok(result);
        }
    }
}
