using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFresherPublicationRetryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicationError",
                table: "AcquisitionImportItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationStatus",
                table: "AcquisitionImportItems",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "NotRequested");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicationError",
                table: "AcquisitionImportItems");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "AcquisitionImportItems");
        }
    }
}
