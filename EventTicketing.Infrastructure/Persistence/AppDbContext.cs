using EventTicketing.Domain.Entities;
using EventTicketing.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;

namespace EventTicketing.Infrastructure.Persistence
{
    public class AppDbContext : IdentityDbContext<EventTicketingUser, IdentityRole<Guid>, Guid>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<TicketType> TicketTypes { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<EventTicketingUser>(entity =>
            {
                entity.Property(u => u.OrganizationName).HasMaxLength(200);
            });

            builder.Entity<Wallet>(entity =>
            {
                entity.ToTable("Wallets");
                entity.HasKey(w => w.Id);

                entity.Property(w => w.Balance).HasPrecision(18, 2);

                entity.HasOne<EventTicketingUser>()
                      .WithOne(u => u.Wallet)
                      .HasForeignKey<Wallet>(w => w.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(w => w.UserId).IsUnique();

                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_Wallets_Balance_NonNegative", "\"Balance\" >= 0"));
            });

            builder.Entity<WalletTransaction>(entity =>
            {
                entity.ToTable("WalletTransactions");
                entity.HasKey(t => t.Id);

                entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
                entity.Property(t => t.Reference).HasMaxLength(50).IsRequired();
                entity.Property(t => t.Amount).HasPrecision(18, 2);
                entity.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");

                entity.HasOne<EventTicketingUser>()
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(t => new { t.UserId, t.CreatedAt });
            });

            builder.Entity<Event>(entity =>
            {
                entity.ToTable("Events");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(4000).IsRequired();
                entity.Property(e => e.Venue).HasMaxLength(300).IsRequired();
                entity.Property(e => e.StartsAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(e => e.ImagePath).HasMaxLength(500);

                // Stored as a string so the value is readable when you inspect the table in Neon.
                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.RejectionReason).HasMaxLength(1000);

                entity.HasOne<EventTicketingUser>()
                      .WithMany()
                      .HasForeignKey(e => e.OrganizerId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.OrganizerId);
                entity.HasIndex(e => new { e.Status, e.CreatedAt });
                entity.HasIndex(e => e.StartsAt);
            });

            builder.Entity<TicketType>(entity =>
            {
                entity.ToTable("TicketTypes");
                entity.HasKey(t => t.Id);

                entity.Property(t => t.Name).HasMaxLength(100).IsRequired();
                entity.Property(t => t.Price).HasPrecision(18, 2);
                entity.Property(t => t.EarlyBirdPrice).HasPrecision(18, 2);
                entity.Property(t => t.SalesOpenAt).HasColumnType("timestamp with time zone");
                entity.Property(t => t.EarlyBirdEndsAt).HasColumnType("timestamp with time zone");

                entity.HasOne<Event>()
                      .WithMany(e => e.TicketTypes)
                      .HasForeignKey(t => t.EventId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(t => t.EventId);

                // Rules the database itself can enforce, per the design.
                // Column names must be quoted: EF creates them as "Price", and
                // Postgres folds unquoted names to lowercase (price), which then fails.
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_TicketTypes_Price_NonNegative", "\"Price\" >= 0"));
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_TicketTypes_EarlyBirdPrice_NonNegative", "\"EarlyBirdPrice\" IS NULL OR \"EarlyBirdPrice\" >= 0"));
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_TicketTypes_Quantity_Positive", "\"Quantity\" > 0"));
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_TicketTypes_EarlyBird_Pair",
                    "(\"EarlyBirdPrice\" IS NULL AND \"EarlyBirdEndsAt\" IS NULL) OR (\"EarlyBirdPrice\" IS NOT NULL AND \"EarlyBirdEndsAt\" IS NOT NULL)"));
            });

            builder.Entity<Reservation>(entity =>
            {
                entity.ToTable("Reservations");
                entity.HasKey(r => r.Id);

                entity.Property(r => r.PriceAtReservation).HasPrecision(18, 2);
                entity.Property(r => r.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(r => r.ExpiresAt).HasColumnType("timestamp with time zone");
                entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
                entity.Property(r => r.PaymentIdempotencyKey).HasMaxLength(100);
                entity.Property(r => r.PaidAt).HasColumnType("timestamp with time zone");

                entity.HasOne<EventTicketingUser>()
                      .WithMany()
                      .HasForeignKey(r => r.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Event>()
                      .WithMany()
                      .HasForeignKey(r => r.EventId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<TicketType>()
                      .WithMany()
                      .HasForeignKey(r => r.TicketTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(r => new { r.UserId, r.EventId })
                      .IsUnique()
                      .HasDatabaseName("IX_Reservations_UserId_EventId_Active")
                      .HasFilter("\"Status\" IN ('Held', 'Paid')");

                entity.HasIndex(r => new { r.TicketTypeId, r.Status, r.ExpiresAt });
                entity.HasIndex(r => new { r.UserId, r.PaymentIdempotencyKey }).IsUnique();

                entity.HasIndex(r => new { r.Status, r.ExpiresAt })
                      .HasDatabaseName("IX_Reservations_Status_ExpiresAt")
                      .HasFilter("\"Status\" = 'Held'");

                entity.HasIndex(r => new { r.Status, r.ExpiresAt })
                      .HasDatabaseName("IX_Reservations_Status_ExpiresAt_Dead")
                      .HasFilter("\"Status\" IN ('Expired', 'Cancelled')");

                entity.HasIndex(r => r.PaidAt)
                      .HasDatabaseName("IX_Reservations_PaidAt_Idempotency")
                      .HasFilter("\"PaymentIdempotencyKey\" IS NOT NULL");

                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_Reservations_Price_NonNegative", "\"PriceAtReservation\" >= 0"));
                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_Reservations_Expiry_After_Created", "\"ExpiresAt\" > \"CreatedAt\""));
            });

            builder.Entity<Ticket>(entity =>
            {
                entity.ToTable("Tickets");
                entity.HasKey(t => t.Id);

                entity.Property(t => t.Code).HasMaxLength(20).IsRequired();
                entity.Property(t => t.CheckedInAt).HasColumnType("timestamp with time zone");

                entity.HasOne<Reservation>()
                      .WithOne()
                      .HasForeignKey<Ticket>(t => t.ReservationId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<EventTicketingUser>()
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Event>()
                      .WithMany()
                      .HasForeignKey(t => t.EventId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(t => t.ReservationId).IsUnique();
                entity.HasIndex(t => t.Code).IsUnique();
                entity.HasIndex(t => t.UserId);
                entity.HasIndex(t => t.EventId);

                entity.ToTable(tb => tb.HasCheckConstraint(
                    "CK_Tickets_Code_NotEmpty", "length(\"Code\") > 0"));
            });

            builder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(t => t.Id);

                entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
                entity.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
                entity.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");
                entity.Property(t => t.ExpiresAt).HasColumnType("timestamp with time zone");
                entity.Property(t => t.AbsoluteExpiresAt).HasColumnType("timestamp with time zone");
                entity.Property(t => t.RevokedAt).HasColumnType("timestamp with time zone");

                entity.HasOne<EventTicketingUser>()
                      .WithMany()
                      .HasForeignKey(t => t.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(t => t.TokenHash).IsUnique();
                entity.HasIndex(t => t.UserId);
                entity.HasIndex(t => t.FamilyId);
                entity.HasIndex(t => t.ExpiresAt);
                entity.HasIndex(t => t.AbsoluteExpiresAt);
                entity.HasIndex(t => t.RevokedAt);
            });
        }
    }
}
