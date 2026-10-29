using System;

namespace EventTicketing.Application.DTOs
{
    public class MyTicketResponse
    {
        public Guid TicketId { get; set; }
        public string Code { get; set; } = null!;
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public string OrganizerName { get; set; } = string.Empty;
        public string Venue { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public string TicketTypeName { get; set; } = null!;
        public decimal PricePaid { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CheckedInAt { get; set; }
    }
}
