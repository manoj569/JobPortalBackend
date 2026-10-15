using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedCompanyWebsiteDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedWebsiteDomain",
                table: "Companies",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.Sql(@"UPDATE ""Companies""
SET ""NormalizedWebsiteDomain"" = regexp_replace(
    split_part(split_part(replace(replace(lower(trim(""WebsiteUrl"")), 'https://', ''), 'http://', ''), '/', 1), '?', 1),
    '^www[.]', '')
WHERE ""WebsiteUrl"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_NormalizedWebsiteDomain",
                table: "Companies",
                column: "NormalizedWebsiteDomain",
                filter: "\"IsDeleted\" = FALSE AND \"NormalizedWebsiteDomain\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_NormalizedWebsiteDomain",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NormalizedWebsiteDomain",
                table: "Companies");
        }
    }
}
