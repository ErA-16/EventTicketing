using System;
using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class CreateTicketTypeRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = null!;

        [Required]
        [Range(typeof(decimal), "0.01", "1000000")]
        public decimal Price { get; set; }

        [Required]
        [Range(1, 1000000)]
        public int Quantity { get; set; }

        [Required]
        public DateTimeOffset SalesOpenAt { get; set; }

        [Range(typeof(decimal), "0.01", "1000000")]
        public decimal? EarlyBirdPrice { get; set; }

        public DateTimeOffset? EarlyBirdEndsAt { get; set; }
    }
}
