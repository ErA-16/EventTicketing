using System;
using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class CreateEventRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = null!;

        [Required]
        [StringLength(4000, MinimumLength = 10)]
        public string Description { get; set; } = null!;

        [Required]
        [StringLength(300, MinimumLength = 3)]
        public string Venue { get; set; } = null!;

        [Required]
        public DateTimeOffset StartsAt { get; set; }
    }
}
