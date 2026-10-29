using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;

namespace EventTicketing.Application.Interfaces
{
    public interface IStockQueryService
    {
        Task<IReadOnlyDictionary<Guid, TicketTypeStock>> GetTicketTypeStockAsync(
            IReadOnlyCollection<Guid> ticketTypeIds,
            CancellationToken cancellationToken);

        Task<IReadOnlyDictionary<Guid, int>> GetEventTicketsLeftAsync(
            IReadOnlyCollection<Guid> eventIds,
            CancellationToken cancellationToken);
    }
}
