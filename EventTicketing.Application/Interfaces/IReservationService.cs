using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;

namespace EventTicketing.Application.Interfaces
{
    public interface IReservationService
    {
        Task<ReservationResponse> ReserveAsync(Guid userId, Guid eventId, CreateReservationRequest request, CancellationToken cancellationToken);

        Task<PaymentResponse> PayAsync(Guid userId, Guid reservationId, string idempotencyKey, CancellationToken cancellationToken);

        Task<PaymentResponse> PurchaseAsync(Guid userId, Guid eventId, PurchaseTicketRequest request, string idempotencyKey, CancellationToken cancellationToken);

        Task CancelAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken);

        Task<IReadOnlyList<MyReservationResponse>> GetMyReservationsAsync(Guid userId, CancellationToken cancellationToken);
    }
}
