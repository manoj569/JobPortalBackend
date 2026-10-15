using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFresherRunRejectionCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvalidUrlCount",
                table: "AcquisitionImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedExperienceCount",
                table: "AcquisitionImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedGeographyCount",
                table: "AcquisitionImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedInvalidDataCount",
                table: "AcquisitionImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvalidUrlCount",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropColumn(
                name: "RejectedExperienceCount",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropColumn(
                name: "RejectedGeographyCount",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropColumn(
                name: "RejectedInvalidDataCount",
                table: "AcquisitionImportRuns");
        }
    }
}
