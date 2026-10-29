using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/events")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public class EventsController : ControllerBase
    {
        private readonly IEventQueryService _eventQueryService;

        public EventsController(IEventQueryService eventQueryService)
        {
            _eventQueryService = eventQueryService;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] CursorQuery query, CancellationToken cancellationToken)
        {
            var result = await _eventQueryService.GetPublishedAsync(query, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _eventQueryService.GetPublishedByIdAsync(id, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
    }
}
