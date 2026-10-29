using System;
using System.Collections.Generic;

namespace EventTicketing.Application.DTOs
{
    public class EventDetailsResponse
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = null!;

        public string Description { get; set; } = null!;

        public string Venue { get; set; } = null!;

        public DateTime StartsAt { get; set; }

        public string? ImageUrl { get; set; }

        public string OrganizerName { get; set; } = string.Empty;

        public List<TicketTypeResponse> TicketTypes { get; set; } = new List<TicketTypeResponse>();
    }
}
