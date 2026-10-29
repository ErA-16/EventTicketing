using EventTicketing.Domain.Enums;
using System;
using System.Collections.Generic;

namespace EventTicketing.Domain.Entities
{
    public class Event
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrganizerId { get; set; }
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Venue { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ImagePath { get; set; }
        public EventStatus Status { get; set; } = EventStatus.Draft;

        public List<TicketType> TicketTypes { get; set; } = new();

        public string? RejectionReason { get; set; }

        public bool IsEditable => Status == EventStatus.Draft;
    }
}
