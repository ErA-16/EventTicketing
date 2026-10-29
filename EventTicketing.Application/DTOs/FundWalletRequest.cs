using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class FundWalletRequest
    {
        [Range(0.01, 10000, ErrorMessage = "Amount must be between 0.01 and 10000.")]
        public decimal Amount { get; set; }
    }
}
