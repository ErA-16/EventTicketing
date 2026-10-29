using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventTicketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopePaymentIdempotencyKeyToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_PaymentIdempotencyKey",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_UserId_PaymentIdempotencyKey",
                table: "Reservations",
                columns: new[] { "UserId", "PaymentIdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_UserId_PaymentIdempotencyKey",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PaymentIdempotencyKey",
                table: "Reservations",
                column: "PaymentIdempotencyKey",
                unique: true);
        }
    }
}
