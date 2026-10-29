using System;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Application.DTOs
{
    public class ReservationResponse
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public Guid TicketTypeId { get; set; }
        public decimal PriceAtReservation { get; set; }
        public ReservationStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
