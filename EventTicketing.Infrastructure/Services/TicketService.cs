using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Infrastructure.Services
{
    public class TicketService : ITicketService
    {
        private readonly AppDbContext _db;

        private class CursorData
        {
            public DateTime? StartsAt { get; set; }

            public Guid? TicketId { get; set; }
        }

        public TicketService(AppDbContext db)
        {
            _db = db;
        }

        private static int NormalizeLimit(int limit)
        {
            if (limit < 1)
            {
                return 20;
            }

            return limit > 50 ? 50 : limit;
        }

        private static CursorData? ParseCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return null;
            }

            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var cursorData = JsonSerializer.Deserialize<CursorData>(json);
                if (cursorData?.StartsAt is null || cursorData.TicketId is null)
                {
                    throw new RequestValidationException(new[] { "Invalid cursor." });
                }

                return cursorData;
            }
            catch (RequestValidationException)
            {
                throw;
            }
            catch
            {
                throw new RequestValidationException(new[] { "Invalid cursor." });
            }
        }

        private static string EncodeCursor(DateTime startsAt, Guid ticketId)
        {
            var json = JsonSerializer.Serialize(new CursorData
            {
                StartsAt = startsAt,
                TicketId = ticketId
            });

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public async Task<CheckInTicketResponse> CheckInAsync(Guid organizerId, CheckInTicketRequest request, CancellationToken cancellationToken)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                throw new RequestValidationException(new[] { "Ticket code is required." });
            }

            var ticket = await (
                from t in _db.Tickets.AsNoTracking()
                join e in _db.Events.AsNoTracking() on t.EventId equals e.Id
                where t.Code == code && e.OrganizerId == organizerId
                select new
                {
                    TicketId = t.Id,
                    t.Code,
                    t.CheckedInAt,
                    EventId = e.Id,
                    EventTitle = e.Title
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ticket is null)
            {
                throw new NotFoundException("Ticket not found.");
            }

            if (ticket.CheckedInAt is not null)
            {
                throw new ConflictException("Ticket has already been checked in.");
            }

            var now = DateTime.UtcNow;
            var updated = await _db.Tickets
                .Where(t => t.Id == ticket.TicketId && t.CheckedInAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.CheckedInAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated == 0)
            {
                throw new ConflictException("Ticket has already been checked in.");
            }

            return new CheckInTicketResponse
            {
                TicketId = ticket.TicketId,
                Code = ticket.Code,
                EventId = ticket.EventId,
                EventTitle = ticket.EventTitle,
                CheckedInAt = now
            };
        }

        public async Task<CursorPage<MyTicketResponse>> GetMyTicketsAsync(
            Guid attendeeId,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
        {
            limit = NormalizeLimit(limit);
            var cursorData = ParseCursor(cursor);

            var query =
                from t in _db.Tickets.AsNoTracking()
                join r in _db.Reservations.AsNoTracking() on t.ReservationId equals r.Id
                join e in _db.Events.AsNoTracking() on t.EventId equals e.Id
                join tt in _db.TicketTypes.AsNoTracking() on r.TicketTypeId equals tt.Id
                join u in _db.Users.AsNoTracking() on e.OrganizerId equals u.Id
                where t.UserId == attendeeId
                select new MyTicketResponse
                {
                    TicketId = t.Id,
                    Code = t.Code,
                    EventId = e.Id,
                    EventTitle = e.Title,
                    OrganizerName = u.OrganizationName != null && u.OrganizationName != ""
                        ? u.OrganizationName
                        : u.FirstName + " " + u.LastName,
                    Venue = e.Venue,
                    StartsAt = e.StartsAt,
                    TicketTypeName = tt.Name,
                    PricePaid = r.PriceAtReservation,
                    PaidAt = r.PaidAt,
                    CheckedInAt = t.CheckedInAt
                };

            if (cursorData is not null)
            {
                var startsAt = cursorData.StartsAt!.Value;
                var ticketId = cursorData.TicketId!.Value;
                query = query.Where(t =>
                    t.StartsAt > startsAt ||
                    (t.StartsAt == startsAt && t.TicketId.CompareTo(ticketId) > 0));
            }

            var tickets = await query
                .OrderBy(t => t.StartsAt)
                .ThenBy(t => t.TicketId)
                .Take(limit + 1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var hasMore = tickets.Count > limit;
            if (hasMore)
            {
                tickets.RemoveAt(tickets.Count - 1);
            }

            string? nextCursor = null;
            if (hasMore && tickets.Count > 0)
            {
                var last = tickets[tickets.Count - 1];
                nextCursor = EncodeCursor(last.StartsAt, last.TicketId);
            }

            return new CursorPage<MyTicketResponse>
            {
                Items = tickets,
                NextCursor = nextCursor,
                HasMore = hasMore
            };
        }
    }
}
