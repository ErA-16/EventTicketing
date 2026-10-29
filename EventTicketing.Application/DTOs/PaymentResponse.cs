using System;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Application.DTOs
{
    public class PaymentResponse
    {
        public Guid ReservationId { get; set; }
        public Guid EventId { get; set; }
        public ReservationStatus Status { get; set; }
        public decimal PricePaid { get; set; }
        public Guid TicketId { get; set; }
        public string Code { get; set; } = null!;
    }
}
