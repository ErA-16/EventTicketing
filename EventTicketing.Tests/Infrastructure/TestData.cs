using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;

namespace EventTicketing.Tests.Infrastructure
{
    public static class TestData
    {
        public static async Task<Event> CreateEventAsync(
            Guid organizerId,
            string title,
            EventStatus status = EventStatus.Published,
            DateTime? startsAt = null,
            DateTime? createdAt = null,
            string venue = "Test Venue",
            string? imagePath = null,
            TicketTypeSpec[]? ticketTypes = null,
            CancellationToken cancellationToken = default)
        {
            var eventEntity = new Event
            {
                Id = Guid.NewGuid(),
                OrganizerId = organizerId,
                Title = title,
                Description = $"Description for {title}",
                Venue = venue,
                StartsAt = startsAt ?? DateTime.UtcNow.AddDays(7),
                CreatedAt = createdAt ?? DateTime.UtcNow,
                ImagePath = imagePath,
                Status = status
            };

            await using var db = TestDatabase.CreateDbContext();

            if (ticketTypes is not null)
            {
                foreach (var spec in ticketTypes)
                {
                    eventEntity.TicketTypes.Add(new TicketType
                    {
                        Id = Guid.NewGuid(),
                        EventId = eventEntity.Id,
                        Name = spec.Name,
                        Price = spec.Price,
                        EarlyBirdPrice = spec.EarlyBirdPrice,
                        EarlyBirdEndsAt = spec.EarlyBirdEndsAt,
                        Quantity = spec.Quantity,
                        SalesOpenAt = spec.SalesOpenAt ?? DateTime.UtcNow.AddDays(-1)
                    });
                }
            }

            db.Events.Add(eventEntity);
            await db.SaveChangesAsync(cancellationToken);
            return eventEntity;
        }

        public static async Task<Reservation> CreateReservationAsync(
            Guid userId,
            Guid eventId,
            Guid ticketTypeId,
            ReservationStatus status,
            DateTime? expiresAt = null,
            decimal price = 100m,
            CancellationToken cancellationToken = default)
        {
            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EventId = eventId,
                TicketTypeId = ticketTypeId,
                PriceAtReservation = price,
                Status = status,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(10),
                PaidAt = status == ReservationStatus.Paid ? DateTime.UtcNow.AddMinutes(-1) : null
            };

            await using var db = TestDatabase.CreateDbContext();
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync(cancellationToken);
            return reservation;
        }
    }

    public sealed record TicketTypeSpec(
        string Name,
        decimal Price,
        int Quantity,
        decimal? EarlyBirdPrice = null,
        DateTime? EarlyBirdEndsAt = null,
        DateTime? SalesOpenAt = null);
}
