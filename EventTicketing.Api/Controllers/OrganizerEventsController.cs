using Asp.Versioning;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Authorize(Roles = "Organizer")]
    [Route("api/v{version:apiVersion}/organizer/events")]
    public class OrganizerEventsController : ControllerBase
    {
        private readonly IOrganizerEventService _eventService;

        public OrganizerEventsController(IOrganizerEventService eventService)
        {
            _eventService = eventService;
        }
        private Guid GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claim, out var userId))
            {
                throw new ForbiddenException("A valid user identity is required.");
            }

            return userId;
        }

        private string GetApiVersion()
        {
            return HttpContext.RequestedApiVersion?.ToString() ?? "1.0";
        }

        [HttpPost]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create(CreateEventRequest request, CancellationToken cancellationToken)
        {
            var created = await _eventService.CreateAsync(GetUserId(), request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = created.Id, version = GetApiVersion() }, created);
        }

        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<EventResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        {
            var result = await _eventService.GetMineAsync(GetUserId(), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        {
            var result = await _eventService.GetAsync(GetUserId(), id, cancellationToken);
            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
        {
            var updated = await _eventService.UpdateAsync(GetUserId(), id, request, cancellationToken);
            return Ok(updated);
        }

        [HttpPost("{id:guid}/ticket-types")]
        [ProducesResponseType(typeof(TicketTypeResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> AddTicketType(Guid id, CreateTicketTypeRequest request, CancellationToken cancellationToken)
        {
            var created = await _eventService.AddTicketTypeAsync(GetUserId(), id, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id, version = GetApiVersion() }, created);
        }

        [HttpPut("{id:guid}/ticket-types/{ticketTypeId:guid}")]
        [ProducesResponseType(typeof(TicketTypeResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateTicketType(
            Guid id,
            Guid ticketTypeId,
            UpdateTicketTypeRequest request,
            CancellationToken cancellationToken)
        {
            var updated = await _eventService.UpdateTicketTypeAsync(GetUserId(), id, ticketTypeId, request, cancellationToken);
            return Ok(updated);
        }

        [HttpDelete("{id:guid}/ticket-types/{ticketTypeId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteTicketType(Guid id, Guid ticketTypeId, CancellationToken cancellationToken)
        {
            await _eventService.DeleteTicketTypeAsync(GetUserId(), id, ticketTypeId, cancellationToken);
            return NoContent();
        }

        [HttpPost("{id:guid}/submit")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        {
            var result = await _eventService.SubmitAsync(GetUserId(), id, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/publish")]
        [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
        {
            var result = await _eventService.PublishAsync(GetUserId(), id, cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/image")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ImageUploadResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadImage(Guid id, IFormFile? image, CancellationToken cancellationToken)
        {
            if (image == null || image.Length == 0)
            {
                return BadRequest(new
                {
                    code = "validation_failed",
                    message = "One or more fields are invalid.",
                    details = new[] { "An image file is required in the 'image' form field." }
                });
            }

            await using var stream = image.OpenReadStream();
            var result = await _eventService.SetImageAsync(GetUserId(), id, stream, image.ContentType, cancellationToken);
            return Ok(result);
        }
    }
}
