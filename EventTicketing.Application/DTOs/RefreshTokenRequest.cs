using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class RefreshTokenRequest
    {
        [Required]
        [StringLength(200)]
        public string RefreshToken { get; set; } = null!;
    }
}
