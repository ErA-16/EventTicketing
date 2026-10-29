using System;
using System.Collections.Generic;

namespace EventTicketing.Application.DTOs
{
    public class EventResponse
    {
        public Guid Id { get; set; }
        public Guid OrganizerId { get; set; }
        public string OrganizerName { get; set; } = string.Empty;
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Venue { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageUrl { get; set; }
        public string Status { get; set; } = null!;
        public string? RejectionReason { get; set; }
        public int TicketsLeft { get; set; }
        public List<TicketTypeResponse> TicketTypes { get; set; } = new();
    }
}
