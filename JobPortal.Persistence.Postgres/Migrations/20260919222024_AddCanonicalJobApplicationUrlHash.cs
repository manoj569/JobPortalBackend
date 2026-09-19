using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalJobApplicationUrlHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalApplicationUrlHash",
                table: "Jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CanonicalApplicationUrlHash",
                table: "Jobs",
                column: "CanonicalApplicationUrlHash",
                filter: "\"IsDeleted\" = FALSE AND \"CanonicalApplicationUrlHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_CanonicalApplicationUrlHash",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CanonicalApplicationUrlHash",
                table: "Jobs");
        }
    }
}
