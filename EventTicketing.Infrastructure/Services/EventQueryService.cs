using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Application.Mapping;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using EventTicketing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventTicketing.Infrastructure.Services
{
    public class EventQueryService : IEventQueryService
    {
        private readonly AppDbContext _db;
        private readonly EventMapper _mapper;
        private readonly ICacheService _cache;
        private readonly R2Options _r2Options;
        private readonly IStockQueryService _stockQuery;

        public EventQueryService(
            AppDbContext db,
            EventMapper mapper,
            ICacheService cache,
            IOptions<R2Options> r2Options,
            IStockQueryService stockQuery)
        {
            _db = db;
            _mapper = mapper;
            _cache = cache;
            _r2Options = r2Options.Value;
            _stockQuery = stockQuery;
        }

        private string BuildImageUrl(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null!;
            }

            var baseUrl = (_r2Options.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return imagePath;
            }

            return baseUrl + "/" + imagePath.TrimStart('/');
        }

        private decimal GetMinPrice(Event entity)
        {
            if (entity.TicketTypes == null || entity.TicketTypes.Count == 0)
            {
                return 0m;
            }

            var now = DateTime.UtcNow;
            decimal min = decimal.MaxValue;
            foreach (var ticketType in entity.TicketTypes)
            {
                var price = ticketType.GetCurrentPrice(now);
                if (price < min)
                {
                    min = price;
                }
            }

            return min == decimal.MaxValue ? 0m : min;
        }

        private static string BuildListCacheKey(CursorQuery query)
        {
            var q = query.Q ?? string.Empty;
            var cursor = query.Cursor ?? string.Empty;
            var limit = query.Limit;
            var raw = q + "|" + cursor + "|" + limit;
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return hash;
        }

        private async Task<string> GetOrganizerNameAsync(Guid organizerId, CancellationToken cancellationToken)
        {
            var organizer = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == organizerId)
                .Select(u => new { u.FirstName, u.LastName, u.OrganizationName })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (organizer is null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(organizer.OrganizationName)
                ? organizer.OrganizationName.Trim()
                : $"{organizer.FirstName} {organizer.LastName}".Trim();
        }

        private async Task ApplyListStockAsync(CursorPage<EventSummaryResponse> page, CancellationToken cancellationToken)
        {
            if (page.Items.Count == 0)
            {
                return;
            }

            var stock = await _stockQuery
                .GetEventTicketsLeftAsync(page.Items.Select(i => i.Id).ToList(), cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                item.TicketsLeft = stock.GetValueOrDefault(item.Id);
            }
        }

        private async Task ApplyDetailStockAsync(EventDetailsResponse details, CancellationToken cancellationToken)
        {
            if (details.TicketTypes.Count == 0)
            {
                return;
            }

            var stock = await _stockQuery
                .GetTicketTypeStockAsync(details.TicketTypes.Select(t => t.Id).ToList(), cancellationToken)
                .ConfigureAwait(false);

            foreach (var ticketType in details.TicketTypes)
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
            }
        }

        public async Task<CursorPage<EventSummaryResponse>> GetPublishedAsync(CursorQuery query, CancellationToken cancellationToken)
        {
            var cacheKey = EventTicketing.Application.Common.CacheKeys.EventList(BuildListCacheKey(query));
            var cached = await _cache.GetAsync<CursorPage<EventSummaryResponse>>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                await ApplyListStockAsync(cached, cancellationToken).ConfigureAwait(false);
                return cached;
            }

            var limit = query.Limit <= 0 ? 20 : query.Limit;
            if (limit > 50)
            {
                limit = 50;
            }

            var now = DateTime.UtcNow;
            IQueryable<Event> eventsQuery = _db.Events
                .Include(e => e.TicketTypes)
                .Where(e => e.Status == EventStatus.Published
                    && e.StartsAt >= now
                    && !_db.Users.Any(u => u.Id == e.OrganizerId && u.IsSuspended));

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var q = query.Q.Trim();
                eventsQuery = eventsQuery.Where(e => EF.Functions.ILike(e.Title, "%" + q + "%") || EF.Functions.ILike(e.Venue, "%" + q + "%"));
            }

            CursorData? cursorData = null;
            if (!string.IsNullOrWhiteSpace(query.Cursor))
            {
                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(query.Cursor));
                    cursorData = JsonSerializer.Deserialize<CursorData>(json);
                }
                catch
                {
            throw new EventTicketing.Application.Common.RequestValidationException(new[] { "Invalid cursor." });
        }
            }

            if (cursorData != null && cursorData.CreatedAt.HasValue && cursorData.Id.HasValue)
            {
                var cc = cursorData.CreatedAt.Value;
                var cid = cursorData.Id.Value;
                eventsQuery = eventsQuery.Where(e => e.CreatedAt < cc || (e.CreatedAt == cc && e.Id.CompareTo(cid) < 0));
            }

            eventsQuery = eventsQuery
                .OrderByDescending(e => e.CreatedAt)
                .ThenByDescending(e => e.Id);

            var items = await eventsQuery.Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

            string? nextCursor = null;
            if (items.Count > limit)
            {
                var last = items[limit - 1];
                var cd = new CursorData { CreatedAt = last.CreatedAt, Id = last.Id };
                var nextJson = JsonSerializer.Serialize(cd);
                nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(nextJson));
                items = items.Take(limit).ToList();
            }

            var resultItems = new List<EventSummaryResponse>(items.Count);
            foreach (var eventEntity in items)
            {
                var summary = _mapper.ToSummary(eventEntity);
                summary.ImageUrl = BuildImageUrl(eventEntity.ImagePath);
                summary.MinPrice = GetMinPrice(eventEntity);
                resultItems.Add(summary);
            }

            var page = new CursorPage<EventSummaryResponse>
            {
                Items = resultItems,
                NextCursor = nextCursor,
                HasMore = nextCursor != null
            };

            await _cache.SetAsync(cacheKey, page, cancellationToken).ConfigureAwait(false);
            await ApplyListStockAsync(page, cancellationToken).ConfigureAwait(false);
            return page;
        }

        public async Task<EventDetailsResponse> GetPublishedByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var cacheKey = EventTicketing.Application.Common.CacheKeys.EventDetail(id);
            var cached = await _cache.GetAsync<EventDetailsResponse>(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                await ApplyDetailStockAsync(cached, cancellationToken).ConfigureAwait(false);
                return cached;
            }

            var now = DateTime.UtcNow;
            var eventEntity = await _db.Events
                .Include(e => e.TicketTypes)
                .FirstOrDefaultAsync(e => e.Id == id
                    && e.Status == EventStatus.Published
                    && e.StartsAt >= now
                    && !_db.Users.Any(u => u.Id == e.OrganizerId && u.IsSuspended), cancellationToken)
                .ConfigureAwait(false);

            if (eventEntity == null)
            {
                throw new EventTicketing.Application.Common.NotFoundException("Event not found.");
            }

            var details = _mapper.ToDetails(eventEntity);
            details.ImageUrl = BuildImageUrl(eventEntity.ImagePath);
            details.OrganizerName = await GetOrganizerNameAsync(eventEntity.OrganizerId, cancellationToken).ConfigureAwait(false);
            if (details.TicketTypes != null)
            {
                foreach (var tt in details.TicketTypes)
                {
                    tt.CurrentPrice = eventEntity.TicketTypes.FirstOrDefault(x => x.Id == tt.Id)?.GetCurrentPrice(now) ?? tt.CurrentPrice;
                }
            }

            await _cache.SetAsync(cacheKey, details, cancellationToken).ConfigureAwait(false);
            await ApplyDetailStockAsync(details, cancellationToken).ConfigureAwait(false);
            return details;
        }

        private class CursorData
        {
            public DateTime? CreatedAt { get; set; }

            public Guid? Id { get; set; }
        }
    }
}
