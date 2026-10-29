using System;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Domain.Entities
{
    public class WalletTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public WalletTransactionType Type { get; set; }
        public string Reference { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
