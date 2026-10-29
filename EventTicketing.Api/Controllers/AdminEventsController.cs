using Asp.Versioning;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Authorize(Roles = "Admin")]
    [Route("api/v{version:apiVersion}/admin/events")]
    public class AdminEventsController : ControllerBase
    {
        private readonly IAdminEventService _adminEventService;

        public AdminEventsController(IAdminEventService adminEventService)
        {
            _adminEventService = adminEventService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(EventTicketing.Application.Common.Pagination.CursorPage<EventResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetQueue([FromQuery] EventTicketing.Application.Common.Pagination.CursorQuery query, CancellationToken cancellationToken)
        {
            var result = await _adminEventService.GetQueueAsync(query, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/approve")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        {
            var result = await _adminEventService.ApproveAsync(id, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/reject")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Reject(Guid id, [FromBody] RejectEventRequest request, CancellationToken cancellationToken)
        {
            var result = await _adminEventService.RejectAsync(id, request, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/suspend")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
        {
            var result = await _adminEventService.SuspendAsync(id, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/unsuspend")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Unsuspend(Guid id, CancellationToken cancellationToken)
        {
            var result = await _adminEventService.UnsuspendAsync(id, cancellationToken);
            return Ok(result);
        }
    }
}
