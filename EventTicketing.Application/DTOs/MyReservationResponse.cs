using System;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Application.DTOs
{
    public class MyReservationResponse
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public string Venue { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public Guid TicketTypeId { get; set; }
        public string TicketTypeName { get; set; } = null!;
        public ReservationStatus Status { get; set; }
        public decimal PriceAtReservation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public Guid? TicketId { get; set; }
        public string? TicketCode { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public bool IsExpired { get; set; }
    }
}
