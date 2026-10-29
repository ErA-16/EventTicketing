using System;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Application.DTOs
{
    public class WalletTransactionResponse
    {
        public Guid Id { get; set; }
        public WalletTransactionType Type { get; set; }
        public string Reference { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
