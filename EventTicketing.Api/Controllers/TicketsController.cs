using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using EventTicketing.Api.Extensions;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Authorize]
    [Route("api/v{version:apiVersion}")]
    public class TicketsController : ControllerBase
    {
        private readonly ITicketService _ticketService;

        public TicketsController(ITicketService ticketService)
        {
            _ticketService = ticketService;
        }

        [HttpPost("tickets/check-in")]
        [Authorize(Roles = "Organizer")]
        [EnableRateLimiting("checkin")]
        [ProducesResponseType(typeof(CheckInTicketResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(object), StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> CheckIn(CheckInTicketRequest request, CancellationToken cancellationToken)
        {
            var result = await _ticketService.CheckInAsync(User.GetUserId(), request, cancellationToken);
            return Ok(result);
        }

        [HttpGet("me/tickets")]
        [Authorize(Roles = "Attendee")]
        [ProducesResponseType(typeof(CursorPage<MyTicketResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetMyTickets(
            [FromQuery] string? cursor,
            [FromQuery] int limit = 20,
            CancellationToken cancellationToken = default)
        {
            var result = await _ticketService.GetMyTicketsAsync(
                User.GetUserId(),
                cursor,
                limit,
                cancellationToken);
            return Ok(result);
        }
    }
}
