using Asp.Versioning;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Authorize(Roles = "Admin")]
    [Route("api/v{version:apiVersion}/admin/organizers")]
    public class AdminOrganizersController : ControllerBase
    {
        private readonly IAdminOrganizerService _adminOrganizerService;

        public AdminOrganizersController(IAdminOrganizerService adminOrganizerService)
        {
            _adminOrganizerService = adminOrganizerService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(CursorPage<AdminOrganizerResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrganizers([FromQuery] AdminOrganizerQuery query, CancellationToken cancellationToken)
        {
            var result = await _adminOrganizerService.GetOrganizersAsync(query, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/suspend")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
        {
            await _adminOrganizerService.SuspendAsync(id, cancellationToken);
            return NoContent();
        }

        [HttpPost("{id:guid}/unsuspend")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Unsuspend(Guid id, CancellationToken cancellationToken)
        {
            await _adminOrganizerService.UnsuspendAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
