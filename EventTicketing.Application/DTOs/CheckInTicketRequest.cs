using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class CheckInTicketRequest
    {
        [Required]
        [StringLength(20, MinimumLength = 1)]
        public string Code { get; set; } = null!;
    }
}
