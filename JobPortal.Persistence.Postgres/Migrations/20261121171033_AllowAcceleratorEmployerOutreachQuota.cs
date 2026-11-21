using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AllowAcceleratorEmployerOutreachQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorUsage_State",
                table: "AcceleratorUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorAllowance_Bounds",
                table: "AcceleratorAllowances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorUsage_State",
                table: "AcceleratorUsageReservations",
                sql: "\"State\" BETWEEN 1 AND 5 AND \"Feature\" BETWEEN 1 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorAllowance_Bounds",
                table: "AcceleratorAllowances",
                sql: "\"Allocated\" BETWEEN 1 AND 10000 AND \"Feature\" BETWEEN 1 AND 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorUsage_State",
                table: "AcceleratorUsageReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorAllowance_Bounds",
                table: "AcceleratorAllowances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorUsage_State",
                table: "AcceleratorUsageReservations",
                sql: "\"State\" BETWEEN 1 AND 5 AND \"Feature\" BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorAllowance_Bounds",
                table: "AcceleratorAllowances",
                sql: "\"Allocated\" BETWEEN 1 AND 10000 AND \"Feature\" BETWEEN 1 AND 5");
        }
    }
}
