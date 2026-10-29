using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventTicketing.Infrastructure.Services
{
    public class WalletService : IWalletService
    {
        private readonly AppDbContext _db;

        public WalletService(AppDbContext db)
        {
            _db = db;
        }

        private async Task<Wallet> GetOrCreateWalletAsync(Guid userId, CancellationToken cancellationToken)
        {
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken).ConfigureAwait(false);
            if (wallet != null)
            {
                return wallet;
            }

            wallet = new Wallet
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Balance = 0m
            };

            _db.Wallets.Add(wallet);
            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return wallet;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                _db.Entry(wallet).State = EntityState.Detached;
                return await _db.Wallets.FirstAsync(w => w.UserId == userId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<WalletResponse> GetBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var wallet = await GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);

            var transactions = await _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(20)
                .Select(t => new WalletTransactionResponse
                {
                    Id = t.Id,
                    Type = t.Type,
                    Reference = t.Reference,
                    Amount = t.Amount,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new WalletResponse
            {
                Balance = wallet.Balance,
                Transactions = transactions
            };
        }

        public async Task<WalletResponse> FundAsync(Guid userId, decimal amount, CancellationToken cancellationToken = default)
        {
            if (amount <= 0 || amount > 10000)
            {
                throw new RequestValidationException(new[] { "Amount must be between 0.01 and 10000." });
            }

            var wallet = await GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);

            await using var transaction = await _db.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await _db.Wallets
                .Where(w => w.Id == wallet.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Balance, w => w.Balance + amount), cancellationToken)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                throw new ConflictException("Wallet could not be funded.");
            }

            _db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = WalletTransactionType.Fund,
                Reference = "FUND-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant(),
                Amount = amount,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await _db.Entry(wallet).ReloadAsync(cancellationToken).ConfigureAwait(false);

            return await GetBalanceAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        public async Task DebitAsync(Guid userId, decimal amount, CancellationToken cancellationToken = default)
        {
            if (amount <= 0)
            {
                throw new RequestValidationException(new[] { "Amount must be greater than zero." });
            }

            var updated = await _db.Wallets
                .Where(w => w.UserId == userId && w.Balance >= amount)
                .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Balance, w => w.Balance - amount), cancellationToken)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                throw new ConflictException("Insufficient wallet balance.");
            }
        }
    }
}
