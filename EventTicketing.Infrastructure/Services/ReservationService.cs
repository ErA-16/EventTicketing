using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EventTicketing.Infrastructure.Services
{
    public class ReservationService : IReservationService
    {
        private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);
        private const int MaxTicketCodeAttempts = 5;
        private const string TicketCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly AppDbContext _db;
        private readonly IWalletService _walletService;

        public ReservationService(AppDbContext db, IWalletService walletService)
        {
            _db = db;
            _walletService = walletService;
        }

        private static string GenerateTicketCode()
        {
            Span<char> buffer = stackalloc char[8];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = TicketCodeAlphabet[RandomNumberGenerator.GetInt32(TicketCodeAlphabet.Length)];
            }

            return new string(buffer);
        }

        private static bool IsUniqueViolation(DbUpdateException ex, string constraintName)
        {
            return ex.InnerException is PostgresException { SqlState: "23505" } pg
                && pg.ConstraintName == constraintName;
        }

        private static PaymentResponse ToPaymentResponse(Reservation reservation, Ticket ticket)
        {
            return new PaymentResponse
            {
                ReservationId = reservation.Id,
                EventId = reservation.EventId,
                Status = reservation.Status,
                PricePaid = reservation.PriceAtReservation,
                TicketId = ticket.Id,
                Code = ticket.Code
            };
        }

        private async Task<(Reservation Reservation, Ticket Ticket)?> FindPaidByIdempotencyKeyAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var reservation = await _db.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.PaymentIdempotencyKey == idempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (reservation is null || reservation.Status != ReservationStatus.Paid)
            {
                return null;
            }

            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReservationId == reservation.Id, cancellationToken)
                .ConfigureAwait(false);

            if (ticket is null)
            {
                return null;
            }

            return (reservation, ticket);
        }

        private static void EnsureIdempotencyKeyNotExpired(Reservation reservation, DateTime now)
        {
            var paidAt = reservation.PaidAt ?? reservation.CreatedAt;
            if (now - paidAt >= IdempotencyRetention)
            {
                throw new IdempotencyKeyExpiredException("This idempotency key has expired.");
            }
        }

        private static void EnsureReplayMatches(Reservation reservation, Guid eventId, Guid ticketTypeId)
        {
            if (reservation.EventId != eventId || reservation.TicketTypeId != ticketTypeId)
            {
                throw new ConflictException("This idempotency key was already used for a different purchase.");
            }
        }

        private async Task LockWalletAsync(Guid userId, CancellationToken cancellationToken)
        {
            await _db.Wallets
                .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"UserId\" = {userId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task DebitIfNeededAsync(Guid userId, decimal amount, CancellationToken cancellationToken)
        {
            if (amount > 0)
            {
                await _walletService.DebitAsync(userId, amount, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<Ticket> IssueTicketAsync(
            Reservation reservation,
            Guid userId,
            string idempotencyKey,
            DateTime now,
            IDbContextTransaction transaction,
            CancellationToken cancellationToken)
        {
            reservation.Status = ReservationStatus.Paid;
            reservation.PaymentIdempotencyKey = idempotencyKey;
            reservation.PaidAt = now;

            var ticket = new Ticket
            {
                Id = Guid.NewGuid(),
                ReservationId = reservation.Id,
                UserId = userId,
                EventId = reservation.EventId,
                Code = GenerateTicketCode()
            };
            _db.Tickets.Add(ticket);

            for (var attempt = 1; ; attempt++)
            {
                await transaction.CreateSavepointAsync("ticket_code_attempt", cancellationToken).ConfigureAwait(false);

                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Tickets_Code"))
                {
                    if (attempt >= MaxTicketCodeAttempts)
                    {
                        throw new ConflictException("Could not issue a ticket code. Please try again.");
                    }

                    await transaction.RollbackToSavepointAsync("ticket_code_attempt", cancellationToken).ConfigureAwait(false);
                    _db.Entry(ticket).State = EntityState.Detached;
                    ticket = new Ticket
                    {
                        Id = Guid.NewGuid(),
                        ReservationId = reservation.Id,
                        UserId = userId,
                        EventId = reservation.EventId,
                        Code = GenerateTicketCode()
                    };
                    _db.Tickets.Add(ticket);
                }
            }

            if (reservation.PriceAtReservation > 0)
            {
                _db.WalletTransactions.Add(new WalletTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = WalletTransactionType.Purchase,
                    Reference = ticket.Code,
                    Amount = -reservation.PriceAtReservation,
                    CreatedAt = now
                });

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return ticket;
        }

        public async Task<ReservationResponse> ReserveAsync(Guid userId, Guid eventId, CreateReservationRequest request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var eventEntity = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
            if (eventEntity is null || eventEntity.Status != EventStatus.Published)
            {
                throw new NotFoundException("Event not found.");
            }

            var organizerSuspended = await _db.Users.AnyAsync(u => u.Id == eventEntity.OrganizerId && u.IsSuspended, cancellationToken);
            if (organizerSuspended)
            {
                throw new NotFoundException("Event not found.");
            }

            var ticketType = await _db.TicketTypes.FirstOrDefaultAsync(t => t.Id == request.TicketTypeId && t.EventId == eventId, cancellationToken);
            if (ticketType is null)
            {
                throw new NotFoundException("Ticket type not found.");
            }

            if (now < ticketType.SalesOpenAt)
            {
                throw new ConflictException("Ticket sales have not opened yet.");
            }

            if (now >= eventEntity.StartsAt)
            {
                throw new ConflictException("Ticket sales have closed.");
            }

            var alreadyReserved = await _db.Reservations.AnyAsync(r => r.UserId == userId && r.EventId == eventId, cancellationToken);
            if (alreadyReserved)
            {
                throw new ConflictException("You already have a ticket for this event.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var lockedTicketType = await _db.TicketTypes
                    .FromSqlInterpolated($"SELECT * FROM \"TicketTypes\" WHERE \"Id\" = {ticketType.Id} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);

                if (lockedTicketType is null)
                {
                    throw new NotFoundException("Ticket type not found.");
                }

                now = DateTime.UtcNow;

                var taken = await _db.Reservations.CountAsync(r =>
                    r.TicketTypeId == lockedTicketType.Id &&
                    (r.Status == ReservationStatus.Paid ||
                     (r.Status == ReservationStatus.Held && r.ExpiresAt > now)), cancellationToken);

                if (taken >= lockedTicketType.Quantity)
                {
                    throw new ConflictException("Sold out.");
                }

                var price = lockedTicketType.GetCurrentPrice(now);
                var reservation = new Reservation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    EventId = eventId,
                    TicketTypeId = lockedTicketType.Id,
                    PriceAtReservation = price,
                    Status = ReservationStatus.Held,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(10)
                };

                _db.Reservations.Add(reservation);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new ReservationResponse
                {
                    Id = reservation.Id,
                    EventId = reservation.EventId,
                    TicketTypeId = reservation.TicketTypeId,
                    PriceAtReservation = reservation.PriceAtReservation,
                    Status = reservation.Status,
                    CreatedAt = reservation.CreatedAt,
                    ExpiresAt = reservation.ExpiresAt
                };
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new ConflictException("You already have a ticket for this event.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<PaymentResponse> PayAsync(Guid userId, Guid reservationId, string idempotencyKey, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var reservation = await _db.Reservations
                .FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"Id\" = {reservationId} AND \"UserId\" = {userId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            now = DateTime.UtcNow;

            if (reservation is null)
            {
                throw new NotFoundException("Reservation not found.");
            }

            if (reservation.Status == ReservationStatus.Paid)
            {
                if (reservation.PaymentIdempotencyKey != idempotencyKey)
                {
                    throw new ConflictException("Reservation is already paid.");
                }

                EnsureIdempotencyKeyNotExpired(reservation, now);

                var existingTicket = await _db.Tickets
                    .AsNoTracking()
                    .FirstAsync(t => t.ReservationId == reservation.Id, cancellationToken)
                    .ConfigureAwait(false);

                return ToPaymentResponse(reservation, existingTicket);
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException("Reservation has been cancelled.");
            }

            if (reservation.Status == ReservationStatus.Expired || reservation.ExpiresAt <= now)
            {
                throw new GoneException("Reservation has expired.");
            }

            await DebitIfNeededAsync(userId, reservation.PriceAtReservation, cancellationToken).ConfigureAwait(false);

            Ticket ticket;
            try
            {
                ticket = await IssueTicketAsync(reservation, userId, idempotencyKey, now, transaction, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Reservations_UserId_PaymentIdempotencyKey"))
            {
                throw new ConflictException("This idempotency key has already been used.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToPaymentResponse(reservation, ticket);
        }

        public async Task<PaymentResponse> PurchaseAsync(Guid userId, Guid eventId, PurchaseTicketRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var replay = await FindPaidByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                EnsureReplayMatches(replay.Value.Reservation, eventId, request.TicketTypeId);
                EnsureIdempotencyKeyNotExpired(replay.Value.Reservation, now);
                return ToPaymentResponse(replay.Value.Reservation, replay.Value.Ticket);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var active = await _db.Reservations
                .FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"UserId\" = {userId} AND \"EventId\" = {eventId} AND \"Status\" IN ('Held', 'Paid') FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            now = DateTime.UtcNow;

            if (active is not null)
            {
                if (active.Status == ReservationStatus.Paid)
                {
                    if (active.PaymentIdempotencyKey != idempotencyKey)
                    {
                        throw new ConflictException("You already have a ticket for this event.");
                    }

                    EnsureIdempotencyKeyNotExpired(active, now);

                    var paidTicket = await _db.Tickets
                        .AsNoTracking()
                        .FirstAsync(t => t.ReservationId == active.Id, cancellationToken)
                        .ConfigureAwait(false);

                    return ToPaymentResponse(active, paidTicket);
                }

                if (active.ExpiresAt > now)
                {
                    if (active.TicketTypeId != request.TicketTypeId)
                    {
                        throw new ConflictException("Your held reservation is for a different ticket type.");
                    }

                    await DebitIfNeededAsync(userId, active.PriceAtReservation, cancellationToken).ConfigureAwait(false);

                    Ticket heldTicket;
                    try
                    {
                        heldTicket = await IssueTicketAsync(active, userId, idempotencyKey, now, transaction, cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Reservations_UserId_PaymentIdempotencyKey"))
                    {
                        throw new ConflictException("This idempotency key has already been used.");
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return ToPaymentResponse(active, heldTicket);
                }

                active.Status = ReservationStatus.Expired;
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                now = DateTime.UtcNow;
            }

            var cancelled = await _db.Reservations.AnyAsync(
                r => r.UserId == userId && r.EventId == eventId && r.Status == ReservationStatus.Cancelled,
                cancellationToken).ConfigureAwait(false);

            if (cancelled)
            {
                throw new ConflictException("You already have a reservation for this event.");
            }

            var eventEntity = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken).ConfigureAwait(false);
            if (eventEntity is null || eventEntity.Status != EventStatus.Published)
            {
                throw new NotFoundException("Event not found.");
            }

            var organizerSuspended = await _db.Users.AnyAsync(u => u.Id == eventEntity.OrganizerId && u.IsSuspended, cancellationToken).ConfigureAwait(false);
            if (organizerSuspended)
            {
                throw new NotFoundException("Event not found.");
            }

            var ticketType = await _db.TicketTypes.FirstOrDefaultAsync(t => t.Id == request.TicketTypeId && t.EventId == eventId, cancellationToken).ConfigureAwait(false);
            if (ticketType is null)
            {
                throw new NotFoundException("Ticket type not found.");
            }

            if (now < ticketType.SalesOpenAt)
            {
                throw new ConflictException("Ticket sales have not opened yet.");
            }

            if (now >= eventEntity.StartsAt)
            {
                throw new ConflictException("Ticket sales have closed.");
            }

            await LockWalletAsync(userId, cancellationToken).ConfigureAwait(false);
            now = DateTime.UtcNow;

            replay = await FindPaidByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                EnsureReplayMatches(replay.Value.Reservation, eventId, request.TicketTypeId);
                EnsureIdempotencyKeyNotExpired(replay.Value.Reservation, now);
                return ToPaymentResponse(replay.Value.Reservation, replay.Value.Ticket);
            }

            var price = ticketType.GetCurrentPrice(now);
            await DebitIfNeededAsync(userId, price, cancellationToken).ConfigureAwait(false);

            var lockedTicketType = await _db.TicketTypes
                .FromSqlInterpolated($"SELECT * FROM \"TicketTypes\" WHERE \"Id\" = {ticketType.Id} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (lockedTicketType is null)
            {
                throw new NotFoundException("Ticket type not found.");
            }

            now = DateTime.UtcNow;

            var reservedCount = await _db.Reservations.CountAsync(r =>
                r.TicketTypeId == lockedTicketType.Id &&
                (r.Status == ReservationStatus.Paid ||
                 (r.Status == ReservationStatus.Held && r.ExpiresAt > now)), cancellationToken).ConfigureAwait(false);

            if (reservedCount >= lockedTicketType.Quantity)
            {
                throw new ConflictException("Sold out.");
            }

            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EventId = eventId,
                TicketTypeId = lockedTicketType.Id,
                PriceAtReservation = price,
                Status = ReservationStatus.Held,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            };
            _db.Reservations.Add(reservation);

            Ticket ticket;
            try
            {
                ticket = await IssueTicketAsync(reservation, userId, idempotencyKey, now, transaction, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (
                IsUniqueViolation(ex, "IX_Reservations_UserId_PaymentIdempotencyKey") ||
                IsUniqueViolation(ex, "IX_Reservations_UserId_EventId_Active"))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                var winner = await FindPaidByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken).ConfigureAwait(false);
                if (winner is not null)
                {
                    EnsureReplayMatches(winner.Value.Reservation, eventId, request.TicketTypeId);
                    EnsureIdempotencyKeyNotExpired(winner.Value.Reservation, now);
                    return ToPaymentResponse(winner.Value.Reservation, winner.Value.Ticket);
                }

                throw new ConflictException("You already have a reservation for this event.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToPaymentResponse(reservation, ticket);
        }

        public async Task CancelAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var reservation = await _db.Reservations
                .FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"Id\" = {reservationId} AND \"UserId\" = {userId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            now = DateTime.UtcNow;

            if (reservation is null)
            {
                throw new NotFoundException("Reservation not found.");
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                return;
            }

            if (reservation.Status == ReservationStatus.Paid)
            {
                throw new ConflictException("Paid reservations cannot be cancelled.");
            }

            if (reservation.Status == ReservationStatus.Expired || reservation.ExpiresAt <= now)
            {
                throw new GoneException("Reservation has expired.");
            }

            reservation.Status = ReservationStatus.Cancelled;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<MyReservationResponse>> GetMyReservationsAsync(Guid userId, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var reservations = await (
                from r in _db.Reservations.AsNoTracking()
                join e in _db.Events.AsNoTracking() on r.EventId equals e.Id
                join tt in _db.TicketTypes.AsNoTracking() on r.TicketTypeId equals tt.Id
                join t in _db.Tickets.AsNoTracking() on r.Id equals t.ReservationId into tickets
                from t in tickets.DefaultIfEmpty()
                where r.UserId == userId
                orderby r.CreatedAt descending, r.Id descending
                select new MyReservationResponse
                {
                    Id = r.Id,
                    EventId = r.EventId,
                    EventTitle = e.Title,
                    Venue = e.Venue,
                    StartsAt = e.StartsAt,
                    TicketTypeId = r.TicketTypeId,
                    TicketTypeName = tt.Name,
                    Status = r.Status,
                    PriceAtReservation = r.PriceAtReservation,
                    CreatedAt = r.CreatedAt,
                    ExpiresAt = r.ExpiresAt,
                    PaidAt = r.PaidAt,
                    TicketId = t == null ? (Guid?)null : t.Id,
                    TicketCode = t == null ? null : t.Code,
                    CheckedInAt = t == null ? null : t.CheckedInAt,
                    IsExpired = r.Status == ReservationStatus.Expired ||
                                (r.Status == ReservationStatus.Held && r.ExpiresAt <= now)
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return reservations;
        }
    }
}
