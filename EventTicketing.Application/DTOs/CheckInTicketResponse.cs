using System;

namespace EventTicketing.Application.DTOs
{
    public class CheckInTicketResponse
    {
        public Guid TicketId { get; set; }
        public string Code { get; set; } = null!;
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime CheckedInAt { get; set; }
    }
}
