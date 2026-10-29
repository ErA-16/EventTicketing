using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using EventTicketing.Api.Extensions;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}")]
    [Authorize(Roles = "Attendee")]
    public class ReservationsController : ControllerBase
    {
        private readonly IReservationService _reservationService;

        public ReservationsController(IReservationService reservationService)
        {
            _reservationService = reservationService;
        }

        private static string RequireIdempotencyKey(string? idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            {
                throw new RequestValidationException(new[]
                {
                    "Idempotency-Key header is required and must be 100 characters or fewer."
                });
            }

            return idempotencyKey.Trim();
        }

        [HttpPost("events/{eventId:guid}/reservations")]
        [EnableRateLimiting("reserve")]
        public async Task<IActionResult> Reserve(Guid eventId, [FromBody] CreateReservationRequest request, CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var result = await _reservationService.ReserveAsync(userId, eventId, request, cancellationToken);
            return Created(string.Empty, result);
        }

        [HttpPost("reservations/{reservationId:guid}/payment")]
        [EnableRateLimiting("pay")]
        public async Task<IActionResult> Pay(
            Guid reservationId,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var key = RequireIdempotencyKey(idempotencyKey);
            var userId = User.GetUserId();
            var result = await _reservationService.PayAsync(userId, reservationId, key, cancellationToken);
            return Ok(result);
        }

        [HttpPost("events/{eventId:guid}/purchase")]
        [EnableRateLimiting("pay")]
        public async Task<IActionResult> Purchase(
            Guid eventId,
            [FromBody] PurchaseTicketRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var key = RequireIdempotencyKey(idempotencyKey);
            var userId = User.GetUserId();
            var result = await _reservationService.PurchaseAsync(userId, eventId, request, key, cancellationToken);
            return Created(string.Empty, result);
        }

        [HttpDelete("reservations/{reservationId:guid}")]
        public async Task<IActionResult> Cancel(Guid reservationId, CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            await _reservationService.CancelAsync(userId, reservationId, cancellationToken);
            return NoContent();
        }

        [HttpGet("me/reservations")]
        public async Task<IActionResult> GetMyReservations(CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var result = await _reservationService.GetMyReservationsAsync(userId, cancellationToken);
            return Ok(result);
        }
    }
}
