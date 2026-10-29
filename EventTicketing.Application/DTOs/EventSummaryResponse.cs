using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EventTicketing.Application.DTOs
{
    public class EventSummaryResponse
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = null!;

        public string Venue { get; set; } = null!;

        public DateTime StartsAt { get; set; }

        public string? ImageUrl { get; set; }

        public decimal MinPrice { get; set; }

        public int TicketsLeft { get; set; }
    }
}
