using System;

namespace EventTicketing.Domain.Entities
{
    public class Ticket
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ReservationId { get; set; }
        public Guid UserId { get; set; }
        public Guid EventId { get; set; }
        public string Code { get; set; } = null!;
        public DateTime? CheckedInAt { get; set; }
    }
}
