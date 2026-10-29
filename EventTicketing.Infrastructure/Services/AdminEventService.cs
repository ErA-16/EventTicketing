using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Application.Mapping;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using EventTicketing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Infrastructure.Services
{
    public class AdminEventService : IAdminEventService
    {
        private readonly AppDbContext _db;
        private readonly EventMapper _mapper;
        private readonly ICacheService _cache;
        private readonly IStockQueryService _stockQuery;
        private readonly R2Options _r2Options;

        public AdminEventService(
            AppDbContext db,
            EventMapper mapper,
            ICacheService cache,
            IStockQueryService stockQuery,
            IOptions<R2Options> r2Options)
        {
            _db = db;
            _mapper = mapper;
            _cache = cache;
            _stockQuery = stockQuery;
            _r2Options = r2Options.Value;
        }

        private async Task InvalidateEventAsync(Guid eventId, CancellationToken cancellationToken)
        {
            try
            {
                await _cache.RemoveAsync(CacheKeys.EventDetail(eventId), cancellationToken).ConfigureAwait(false);
                await _cache.RemoveByPrefixAsync(CacheKeys.ListPrefix, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private string? BuildImageUrl(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var baseUrl = (_r2Options.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return imagePath;
            }

            return baseUrl + "/" + imagePath.TrimStart('/');
        }

        private static void ApplyStock(EventResponse response, IReadOnlyDictionary<Guid, TicketTypeStock> stock)
        {
            var total = 0;
            foreach (var ticketType in response.TicketTypes)
            {
                if (stock.TryGetValue(ticketType.Id, out var value))
                {
                    ticketType.Sold = value.Sold;
                    ticketType.Held = value.Held;
                    ticketType.Remaining = value.Remaining;
                }
                else
                {
                    ticketType.Sold = 0;
                    ticketType.Held = 0;
                    ticketType.Remaining = ticketType.Quantity;
                }

                total += ticketType.Remaining;
            }

            response.TicketsLeft = total;
        }

        private async Task HydrateAsync(IReadOnlyList<EventResponse> responses, CancellationToken cancellationToken)
        {
            if (responses.Count == 0)
            {
                return;
            }

            var organizerIds = responses.Select(r => r.OrganizerId).Distinct().ToList();
            var organizers = await _db.Users
                .AsNoTracking()
                .Where(u => organizerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.OrganizationName })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var names = organizers.ToDictionary(
                u => u.Id,
                u => !string.IsNullOrWhiteSpace(u.OrganizationName)
                    ? u.OrganizationName.Trim()
                    : $"{u.FirstName} {u.LastName}".Trim());

            foreach (var response in responses)
            {
                response.ImageUrl = BuildImageUrl(response.ImagePath);
                response.OrganizerName = names.GetValueOrDefault(response.OrganizerId, string.Empty);
            }

            var ticketTypeIds = responses
                .SelectMany(r => r.TicketTypes)
                .Select(t => t.Id)
                .Distinct()
                .ToList();

            var stock = await _stockQuery.GetTicketTypeStockAsync(ticketTypeIds, cancellationToken).ConfigureAwait(false);
            foreach (var response in responses)
            {
                ApplyStock(response, stock);
            }
        }

        private async Task<EventResponse> HydrateAsync(EventResponse response, CancellationToken cancellationToken)
        {
            await HydrateAsync(new[] { response }, cancellationToken).ConfigureAwait(false);
            return response;
        }

        private class CursorData
        {
            public Guid Id { get; set; }
        }

        public async Task<CursorPage<EventResponse>> GetQueueAsync(CursorQuery query, CancellationToken cancellationToken = default)
        {
            if (query is null) query = new CursorQuery();

            var limit = query.Limit;
            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            var allStatuses = string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase);
            EventStatus eventStatus = EventStatus.PendingApproval;
            if (!allStatuses && !string.IsNullOrWhiteSpace(query.Status))
            {
                if (!Enum.TryParse<EventStatus>(query.Status, true, out eventStatus))
                    throw new RequestValidationException(new[] { "Invalid status." });
            }

            var queryable = _db.Events
                .Include(e => e.TicketTypes)
                .AsQueryable();

            if (!allStatuses)
            {
                queryable = queryable.Where(e => e.Status == eventStatus);
            }

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var q = query.Q.Trim();
                queryable = queryable.Where(e =>
                    EF.Functions.ILike(e.Title, $"%{q}%")
                    || EF.Functions.ILike(e.Venue, $"%{q}%")
                    || _db.Users.Any(u => u.Id == e.OrganizerId
                        && (EF.Functions.ILike(u.FirstName, $"%{q}%")
                            || EF.Functions.ILike(u.LastName, $"%{q}%")
                            || (u.OrganizationName != null
                                && EF.Functions.ILike(u.OrganizationName, $"%{q}%")))));
            }

            queryable = queryable.OrderByDescending(e => e.Id);

            if (!string.IsNullOrWhiteSpace(query.Cursor))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(query.Cursor));
                    var cursorData = JsonSerializer.Deserialize<CursorData>(decoded);
                    if (cursorData is null || cursorData.Id == Guid.Empty)
                        throw new RequestValidationException(new[] { "Invalid cursor." });

                    var cursorId = cursorData.Id;
                    queryable = queryable.Where(e => e.Id.CompareTo(cursorId) < 0);
                }
                catch (RequestValidationException) { throw; }
                catch { throw new RequestValidationException(new[] { "Invalid cursor." }); }
            }

            var items = await queryable.Take(limit + 1).ToListAsync(cancellationToken);
            var hasMore = items.Count > limit;
            if (hasMore) items = items.Take(limit).ToList();

            string? nextCursor = null;
            if (hasMore && items.Count > 0)
            {
                var last = items[items.Count - 1];
                var cursorData = new CursorData { Id = last.Id };
                var json = JsonSerializer.Serialize(cursorData);
                nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }

            var responses = items.Select(e => _mapper.ToResponse(e)).ToList();
            await HydrateAsync(responses, cancellationToken).ConfigureAwait(false);

            return new CursorPage<EventResponse>
            {
                Items = responses,
                NextCursor = nextCursor,
                HasMore = hasMore
            };
        }

        public async Task<EventResponse> ApproveAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await _db.Events.Include(e => e.TicketTypes).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (entity is null) throw new NotFoundException("Event not found.");
            if (entity.Status != EventStatus.PendingApproval) throw new ConflictException("Only pending approval events can be approved.");
            entity.Status = EventStatus.Approved;
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventResponse> RejectAsync(Guid eventId, RejectEventRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Reason)) throw new RequestValidationException(new[] { "A reason is required." });
            if (request.Reason.Trim().Length > 1000) throw new RequestValidationException(new[] { "Reason must be 1000 characters or less." });
            var entity = await _db.Events.Include(e => e.TicketTypes).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (entity is null) throw new NotFoundException("Event not found.");
            if (entity.Status != EventStatus.PendingApproval) throw new ConflictException("Only pending approval events can be rejected.");
            entity.Status = EventStatus.Draft;
            entity.RejectionReason = request.Reason.Trim();
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventResponse> SuspendAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await _db.Events.Include(e => e.TicketTypes).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (entity is null) throw new NotFoundException("Event not found.");
            if (entity.Status != EventStatus.Approved && entity.Status != EventStatus.Published)
                throw new ConflictException("Only approved or published events can be suspended.");
            entity.Status = EventStatus.Suspended;
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventResponse> UnsuspendAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await _db.Events.Include(e => e.TicketTypes).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (entity is null) throw new NotFoundException("Event not found.");
            if (entity.Status != EventStatus.Suspended)
                throw new ConflictException("Only suspended events can be unsuspended.");
            entity.Status = EventStatus.Approved;
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);
            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }
    }
}
