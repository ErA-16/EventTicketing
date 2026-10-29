using System.Collections.Generic;

namespace EventTicketing.Application.DTOs
{
    public class WalletResponse
    {
        public decimal Balance { get; set; }

        public List<WalletTransactionResponse> Transactions { get; set; } = new List<WalletTransactionResponse>();
    }
}
