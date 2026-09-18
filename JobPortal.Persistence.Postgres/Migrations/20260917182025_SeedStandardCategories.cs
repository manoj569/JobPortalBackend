using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SeedStandardCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Categories" ("Id", "CreatedAtUtc", "IsDeleted", "Name", "Slug", "DisplayOrder")
                SELECT v."Id", TIMESTAMPTZ '2025-01-01T00:00:00Z', FALSE, v."Name", v."Slug", v."DisplayOrder"
                FROM (VALUES
                    ('10000000-0000-0000-0000-000000000001'::uuid, 'Software Engineering', 'software-engineering', 10),
                    ('10000000-0000-0000-0000-000000000002'::uuid, 'Data Science & Analytics', 'data-science-analytics', 20),
                    ('10000000-0000-0000-0000-000000000003'::uuid, 'AI & Machine Learning', 'ai-machine-learning', 30),
                    ('10000000-0000-0000-0000-000000000004'::uuid, 'DevOps & Cloud Engineering', 'devops-cloud-engineering', 40),
                    ('10000000-0000-0000-0000-000000000005'::uuid, 'Cybersecurity', 'cybersecurity', 50),
                    ('10000000-0000-0000-0000-000000000006'::uuid, 'Quality Assurance & Testing', 'quality-assurance-testing', 60),
                    ('10000000-0000-0000-0000-000000000007'::uuid, 'Product Management', 'product-management', 70),
                    ('10000000-0000-0000-0000-000000000008'::uuid, 'UI/UX Design', 'ui-ux-design', 80),
                    ('10000000-0000-0000-0000-000000000009'::uuid, 'Engineering Management', 'engineering-management', 90),
                    ('10000000-0000-0000-0000-000000000010'::uuid, 'IT Support & Administration', 'it-support-administration', 100)
                ) AS v("Id", "Name", "Slug", "DisplayOrder")
                WHERE NOT EXISTS (SELECT 1 FROM "Categories" c WHERE c."Slug" = v."Slug");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000009"));

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000010"));
        }
    }
}
