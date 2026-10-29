using System;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.DTOs;

namespace EventTicketing.Application.Interfaces
{
    public interface IWalletService
    {
        Task<WalletResponse> GetBalanceAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<WalletResponse> FundAsync(Guid userId, decimal amount, CancellationToken cancellationToken = default);

        Task DebitAsync(Guid userId, decimal amount, CancellationToken cancellationToken = default);
    }
}
