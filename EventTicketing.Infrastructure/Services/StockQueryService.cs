using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Infrastructure.Services
{
    public class StockQueryService : IStockQueryService
    {
        private readonly AppDbContext _db;

        public StockQueryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyDictionary<Guid, TicketTypeStock>> GetTicketTypeStockAsync(
            IReadOnlyCollection<Guid> ticketTypeIds,
            CancellationToken cancellationToken)
        {
            var ids = ticketTypeIds.Distinct().ToList();
            var result = new Dictionary<Guid, TicketTypeStock>();
            if (ids.Count == 0)
            {
                return result;
            }

            var ticketTypes = await _db.TicketTypes
                .AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Quantity })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;
            var counts = await _db.Reservations
                .AsNoTracking()
                .Where(r => ids.Contains(r.TicketTypeId)
                    && (r.Status == ReservationStatus.Paid
                        || (r.Status == ReservationStatus.Held && r.ExpiresAt > now)))
                .GroupBy(r => new { r.TicketTypeId, r.Status })
                .Select(g => new { g.Key.TicketTypeId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var sold = counts
                .Where(c => c.Status == ReservationStatus.Paid)
                .ToDictionary(c => c.TicketTypeId, c => c.Count);
            var held = counts
                .Where(c => c.Status == ReservationStatus.Held)
                .ToDictionary(c => c.TicketTypeId, c => c.Count);

            foreach (var ticketType in ticketTypes)
            {
                var soldCount = sold.GetValueOrDefault(ticketType.Id);
                var heldCount = held.GetValueOrDefault(ticketType.Id);
                result[ticketType.Id] = new TicketTypeStock
                {
                    Sold = soldCount,
                    Held = heldCount,
                    Remaining = Math.Max(0, ticketType.Quantity - soldCount - heldCount)
                };
            }

            return result;
        }

        public async Task<IReadOnlyDictionary<Guid, int>> GetEventTicketsLeftAsync(
            IReadOnlyCollection<Guid> eventIds,
            CancellationToken cancellationToken)
        {
            var ids = eventIds.Distinct().ToList();
            var result = new Dictionary<Guid, int>();
            if (ids.Count == 0)
            {
                return result;
            }

            var ticketTypes = await _db.TicketTypes
                .AsNoTracking()
                .Where(t => ids.Contains(t.EventId))
                .Select(t => new { t.Id, t.EventId, t.Quantity })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var stock = await GetTicketTypeStockAsync(
                ticketTypes.Select(t => t.Id).ToList(),
                cancellationToken).ConfigureAwait(false);

            foreach (var group in ticketTypes.GroupBy(t => t.EventId))
            {
                result[group.Key] = group.Sum(t =>
                    stock.TryGetValue(t.Id, out var value) ? value.Remaining : t.Quantity);
            }

            return result;
        }
    }
}
