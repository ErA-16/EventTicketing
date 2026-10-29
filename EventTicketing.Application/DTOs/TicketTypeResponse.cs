using System;

namespace EventTicketing.Application.DTOs
{
    public class TicketTypeResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal? EarlyBirdPrice { get; set; }
        public DateTime? EarlyBirdEndsAt { get; set; }
        public decimal CurrentPrice { get; set; }
        public int Quantity { get; set; }
        public int Sold { get; set; }
        public int Held { get; set; }
        public int Remaining { get; set; }
        public DateTime SalesOpenAt { get; set; }
    }
}
