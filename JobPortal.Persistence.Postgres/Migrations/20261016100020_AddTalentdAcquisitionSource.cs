using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTalentdAcquisitionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AcquisitionSources" SET "Name" = 'Talentd', "IsEnabled" = TRUE,
                    "AutoPublishEnabled" = TRUE, "IsDeleted" = FALSE
                WHERE "Id" = (SELECT "Id" FROM "AcquisitionSources" WHERE "ProviderKey" = 'talentd'
                    ORDER BY "IsDeleted", "CreatedAtUtc", "Id" LIMIT 1);
                INSERT INTO "AcquisitionSources" ("Id", "ProviderKey", "Name", "IsEnabled", "AutoPublishEnabled", "CreatedAtUtc", "IsDeleted")
                SELECT '7fb8c010-f683-481b-b98f-71e45717f987'::uuid, 'talentd', 'Talentd', TRUE, TRUE,
                    TIMESTAMPTZ '2026-10-16 00:00:00+00', FALSE
                WHERE NOT EXISTS (SELECT 1 FROM "AcquisitionSources" WHERE "ProviderKey" = 'talentd');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve source identity and job/receipt foreign keys on rollback.
            migrationBuilder.Sql("""
                UPDATE "AcquisitionSources" SET "IsEnabled" = FALSE, "AutoPublishEnabled" = FALSE
                WHERE "ProviderKey" = 'talentd';
                """);
        }
    }
}
