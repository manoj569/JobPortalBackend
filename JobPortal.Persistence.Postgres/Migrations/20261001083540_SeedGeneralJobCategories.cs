using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations;

public partial class SeedGeneralJobCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve operator-managed categories, including soft-deleted equivalents.
        migrationBuilder.Sql("""
            INSERT INTO "Categories" ("Id", "CreatedAtUtc", "IsDeleted", "Name", "Slug", "DisplayOrder")
            SELECT v."Id", TIMESTAMPTZ '2025-01-01T00:00:00Z', FALSE, v."Name", v."Slug", v."DisplayOrder"
            FROM (VALUES
                ('10000000-0000-0000-0000-000000000011'::uuid, 'Human Resources & Recruitment', 'human-resources-recruitment', 110, ARRAY['human resources & recruitment','human resources','recruiting','recruitment','talent acquisition']),
                ('10000000-0000-0000-0000-000000000012'::uuid, 'Sales & Business Development', 'sales-business-development', 120, ARRAY['sales & business development','sales','business development']),
                ('10000000-0000-0000-0000-000000000013'::uuid, 'Marketing', 'marketing', 130, ARRAY['marketing']),
                ('10000000-0000-0000-0000-000000000014'::uuid, 'Finance & Accounting', 'finance-accounting', 140, ARRAY['finance & accounting','finance','accounting']),
                ('10000000-0000-0000-0000-000000000015'::uuid, 'Operations', 'operations', 150, ARRAY['operations']),
                ('10000000-0000-0000-0000-000000000016'::uuid, 'Customer Success & Support', 'customer-success-support', 160, ARRAY['customer success & support','customer success','customer support']),
                ('10000000-0000-0000-0000-000000000017'::uuid, 'Legal & Compliance', 'legal-compliance', 170, ARRAY['legal & compliance','legal','compliance'])
            ) AS v("Id", "Name", "Slug", "DisplayOrder", "Aliases")
            WHERE NOT EXISTS (
                SELECT 1 FROM "Categories" c
                WHERE c."Id" = v."Id" OR lower(c."Slug") = v."Slug"
                   OR lower(trim(c."Name")) = ANY(v."Aliases")
                   OR replace(lower(c."Slug"), '-', ' ') = ANY(v."Aliases"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Retain taxonomy data: pre-existing and newly referenced rows cannot
        // safely be distinguished or deleted during rollback.
    }
}
