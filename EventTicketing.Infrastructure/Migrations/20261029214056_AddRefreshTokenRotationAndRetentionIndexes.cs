using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventTicketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenRotationAndRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_Status_ExpiresAt",
                table: "Reservations");

            migrationBuilder.AddColumn<DateTime>(
                name: "AbsoluteExpiresAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "RefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PaidAt_Idempotency",
                table: "Reservations",
                column: "PaidAt",
                filter: "\"PaymentIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ExpiresAt_Dead",
                table: "Reservations",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "\"Status\" IN ('Expired', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AbsoluteExpiresAt",
                table: "RefreshTokens",
                column: "AbsoluteExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_RevokedAt",
                table: "RefreshTokens",
                column: "RevokedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartsAt",
                table: "Events",
                column: "StartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_PaidAt_Idempotency",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Status_ExpiresAt_Dead",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_AbsoluteExpiresAt",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_RevokedAt",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Events_StartsAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AbsoluteExpiresAt",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "RefreshTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ExpiresAt",
                table: "Reservations",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "\"Status\" = 'Held'");
        }
    }
}
