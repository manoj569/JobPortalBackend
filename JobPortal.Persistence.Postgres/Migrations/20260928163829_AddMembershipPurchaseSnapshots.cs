using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPurchaseSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseAmount",
                table: "Payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanName",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnTo",
                table: "Payments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "Payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Payments",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Payments_UnresolvedUserPlan",
                table: "Payments",
                columns: new[] { "UserId", "PlanCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"MembershipId\" IS NOT NULL AND \"PlanCode\" IS NOT NULL AND \"Status\" IN (1, 2, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PurchaseSnapshot",
                table: "Payments",
                sql: "(\"BaseAmount\" IS NULL AND \"TaxRate\" IS NULL AND \"TaxAmount\" IS NULL AND \"PlanName\" IS NULL AND \"DurationDays\" IS NULL)\r\nOR (\"BaseAmount\" IS NOT NULL AND \"TaxRate\" IS NOT NULL AND \"TaxAmount\" IS NOT NULL AND \"PlanName\" IS NOT NULL AND \"DurationDays\" IS NOT NULL\r\n    AND \"PlanCode\" IS NOT NULL AND \"BaseAmount\" > 0 AND \"TaxRate\" BETWEEN 0 AND 100 AND \"TaxAmount\" >= 0\r\n    AND \"DurationDays\" > 0 AND \"TaxAmount\" = round(\"BaseAmount\" * \"TaxRate\" / 100, 2) AND \"Amount\" = \"BaseAmount\" + \"TaxAmount\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Payments_UnresolvedUserPlan",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PurchaseSnapshot",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BaseAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PlanName",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReturnTo",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Payments");
        }
    }
}
