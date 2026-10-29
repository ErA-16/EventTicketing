using System;

namespace EventTicketing.Domain.Entities
{
    public class TicketType
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EventId { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal? EarlyBirdPrice { get; set; }
        public DateTime? EarlyBirdEndsAt { get; set; }
        public int Quantity { get; set; }
        public DateTime SalesOpenAt { get; set; }

        public decimal GetCurrentPrice(DateTime utcNow)
        {
            if (EarlyBirdPrice.HasValue && EarlyBirdEndsAt.HasValue && utcNow < EarlyBirdEndsAt.Value)
            {
                return EarlyBirdPrice.Value;
            }

            return Price;
        }
    }
}
