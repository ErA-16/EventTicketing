using EventTicketing.Application.Common;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Identity;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventTicketing.Infrastructure.Seed
{
    public static class DemoDataSeeder
    {
        private const string DemoOrganizerName = "Demo Nights";

        private static readonly Guid DemoOrganizerId = Guid.Parse("0d3e0000-0000-4000-8000-000000000001");

        private sealed record TicketTierSeed(
            Guid Id,
            string Name,
            decimal Price,
            decimal EarlyBirdPrice,
            int Quantity,
            int EarlyBirdEndsInDays);

        private sealed record EventSeed(
            Guid Id,
            string Title,
            string Description,
            string Venue,
            int StartsInDays,
            int StartHourUtc,
            int CreatedOrder,
            TicketTierSeed[] TicketTypes);

        private static DateTime BuildStartsAt(DateTime now, int days, int hourUtc)
        {
            return DateTime.SpecifyKind(
                now.Date.AddDays(days).AddHours(hourUtc),
                DateTimeKind.Utc);
        }

        private static EventSeed[] BuildEventSeeds()
        {
            return new[]
            {
                new EventSeed(
                    Guid.Parse("0d3e0000-0000-4000-8000-000000000101"),
                    "Neon Frequency",
                    "A rooftop night of Afrobeats, amapiano and neon-lit skyline sets. Three rooms, one skyline, zero dull moments.",
                    "Victoria Island Rooftop, Lagos",
                    46,
                    19,
                    1,
                    new[]
                    {
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000201"), "First Wave", 15000m, 12000m, 100, 14),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000202"), "General Floor", 25000m, 25000m, 70, 0),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000203"), "Backstage", 65000m, 65000m, 30, 0)
                    }),
                new EventSeed(
                    Guid.Parse("0d3e0000-0000-4000-8000-000000000102"),
                    "Punchline Abuja: After Dark",
                    "Stand-up's sharpest voices take the mic for a late-night comedy run in Abuja. Front row is close. Meet-and-greet is closer.",
                    "Transcorp Hilton, Abuja",
                    110,
                    18,
                    2,
                    new[]
                    {
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000211"), "Back Row", 10000m, 8000m, 100, 21),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000212"), "Front Row", 25000m, 25000m, 70, 0),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000213"), "Meet & Greet", 45000m, 45000m, 30, 0)
                    }),
                new EventSeed(
                    Guid.Parse("0d3e0000-0000-4000-8000-000000000103"),
                    "Sunset Ritual",
                    "Golden-hour DJ sets by the water, local food stalls and a slow transition into the night. Bring a friend; stay for the last record.",
                    "Bonny Island Waterfront, Port Harcourt",
                    187,
                    16,
                    3,
                    new[]
                    {
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000221"), "Early Entry", 8000m, 6500m, 100, 30),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000222"), "Sunset Pass", 18000m, 18000m, 70, 0),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000223"), "Cabana Access", 55000m, 55000m, 30, 0)
                    }),
                new EventSeed(
                    Guid.Parse("0d3e0000-0000-4000-8000-000000000104"),
                    "Headphone Party Lagos",
                    "Silent disco on the sand. Three channels, one headset and a beach full of people dancing to different songs. VIP booths include bottle service.",
                    "Landmark Beach, Lagos",
                    245,
                    17,
                    4,
                    new[]
                    {
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000231"), "Early Headset", 8000m, 6000m, 100, 45),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000232"), "Headset + Drink", 15000m, 15000m, 70, 0),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000233"), "VIP Booth", 45000m, 45000m, 30, 0)
                    }),
                new EventSeed(
                    Guid.Parse("0d3e0000-0000-4000-8000-000000000105"),
                    "Culture Yard",
                    "Art, food, fashion and sound in one open-air yard. A full-day showcase for Enugu's makers, storytellers and selectors.",
                    "Milliken Hill Yard, Enugu",
                    334,
                    14,
                    5,
                    new[]
                    {
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000241"), "Community", 5000m, 4000m, 100, 60),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000242"), "Culture Pass", 15000m, 15000m, 70, 0),
                        new TicketTierSeed(Guid.Parse("0d3e0000-0000-4000-8000-000000000243"), "Collector's Circle", 40000m, 40000m, 30, 0)
                    })
            };
        }

        private static async Task<EventTicketingUser?> EnsureOrganizerAsync(
            IServiceProvider serviceProvider,
            string email,
            string password)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<EventTicketingUser>>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");
            var user = await userManager.FindByEmailAsync(email);

            if (user is null)
            {
                user = new EventTicketingUser
                {
                    Id = DemoOrganizerId,
                    FirstName = string.Empty,
                    LastName = string.Empty,
                    OrganizationName = DemoOrganizerName,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Demo organizer seeding failed: {errors}");
                }

                logger.LogInformation("Seeded demo organizer {Email}.", email);
            }
            else if (string.IsNullOrWhiteSpace(user.OrganizationName))
            {
                user.OrganizationName = DemoOrganizerName;
                await userManager.UpdateAsync(user);
            }

            var organizerRole = Role.Organizer.ToString();
            if (!await userManager.IsInRoleAsync(user, organizerRole))
            {
                var roleResult = await userManager.AddToRoleAsync(user, organizerRole);
                if (!roleResult.Succeeded)
                {
                    var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Demo organizer role seeding failed: {errors}");
                }
            }

            return user;
        }

        private static string GetCoverKey(Guid eventId)
        {
            return $"events/{eventId}/cover.jpg";
        }

        private static async Task<bool> SeedEventAsync(
            IServiceProvider serviceProvider,
            EventTicketingUser organizer,
            EventSeed seed,
            DateTime seededAt)
        {
            var db = serviceProvider.GetRequiredService<AppDbContext>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");

            var eventEntity = await db.Events
                .Include(existing => existing.TicketTypes)
                .FirstOrDefaultAsync(existing => existing.Id == seed.Id);

            var changed = false;
            if (eventEntity is null)
            {
                eventEntity = new Event
                {
                    Id = seed.Id,
                    OrganizerId = organizer.Id,
                    Title = seed.Title,
                    Description = seed.Description,
                    Venue = seed.Venue,
                    StartsAt = BuildStartsAt(seededAt, seed.StartsInDays, seed.StartHourUtc),
                    CreatedAt = seededAt.AddSeconds(-seed.CreatedOrder),
                    Status = EventStatus.Published,
                    ImagePath = GetCoverKey(seed.Id)
                };

                var salesOpenAt = seededAt.AddDays(-1);
                foreach (var tier in seed.TicketTypes)
                {
                    eventEntity.TicketTypes.Add(new TicketType
                    {
                        Id = tier.Id,
                        EventId = seed.Id,
                        Name = tier.Name,
                        Price = tier.Price,
                        EarlyBirdPrice = tier.EarlyBirdPrice < tier.Price ? tier.EarlyBirdPrice : null,
                        EarlyBirdEndsAt = tier.EarlyBirdPrice < tier.Price
                            ? seededAt.AddDays(tier.EarlyBirdEndsInDays)
                            : null,
                        Quantity = tier.Quantity,
                        SalesOpenAt = salesOpenAt
                    });
                }

                db.Events.Add(eventEntity);
                await db.SaveChangesAsync();
                changed = true;
                logger.LogInformation("Seeded demo event {Event}.", seed.Title);
            }

            if (string.IsNullOrWhiteSpace(eventEntity.ImagePath))
            {
                eventEntity.ImagePath = GetCoverKey(eventEntity.Id);
                await db.SaveChangesAsync();
                changed = true;
                logger.LogInformation("Linked demo cover for {Event}.", seed.Title);
            }

            return changed;
        }

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");

            var email = configuration["Seed:DemoOrganizerEmail"];
            var password = configuration["Seed:DemoOrganizerPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Demo data seeding skipped: Seed:DemoOrganizerEmail / Seed:DemoOrganizerPassword are not configured.");
                return;
            }

            var organizer = await EnsureOrganizerAsync(serviceProvider, email, password);
            if (organizer is null)
            {
                return;
            }

            var changed = false;
            var seededAt = DateTime.UtcNow;
            foreach (var seed in BuildEventSeeds())
            {
                changed |= await SeedEventAsync(serviceProvider, organizer, seed, seededAt);
            }

            if (!changed)
            {
                return;
            }

            var cache = serviceProvider.GetRequiredService<ICacheService>();
            try
            {
                await cache.RemoveByPrefixAsync(CacheKeys.ListPrefix);
                await cache.RemoveByPrefixAsync(CacheKeys.DetailPrefix);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Demo data seeded but cache invalidation failed.");
            }
        }
    }
}
