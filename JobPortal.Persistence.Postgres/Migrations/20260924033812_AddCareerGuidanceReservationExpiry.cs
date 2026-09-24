using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidanceReservationExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings",
                sql: "\"Status\" BETWEEN 1 AND 8");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings",
                sql: "\"Status\" BETWEEN 1 AND 7");
        }
    }
}
