using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventTicketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeReservationUniquenessActiveOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_UserId_EventId",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_UserId_EventId_Active",
                table: "Reservations",
                columns: new[] { "UserId", "EventId" },
                unique: true,
                filter: "\"Status\" IN ('Held', 'Paid')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_UserId_EventId_Active",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_UserId_EventId",
                table: "Reservations",
                columns: new[] { "UserId", "EventId" },
                unique: true);
        }
    }
}
