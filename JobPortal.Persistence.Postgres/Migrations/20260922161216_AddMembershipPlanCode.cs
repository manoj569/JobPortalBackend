using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPlanCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships");

            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "Memberships",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Backfill existing memberships before creating the new
            // UserId + PlanCode unique index.
            migrationBuilder.Sql(
                """
                UPDATE "Memberships"
                SET "PlanCode" =
                    CASE
                        WHEN LOWER(TRIM("PlanName")) = LOWER('AI Apply Pro')
                            THEN 'AIApplyPro'

                        WHEN LOWER(TRIM("PlanName")) = LOWER('AI Apply')
                            THEN 'AIApply'

                        WHEN LOWER(TRIM("PlanName")) = LOWER('Referral Contact Access')
                            THEN 'ReferralContactAccess'

                        ELSE 'CareerHarborMembership'
                    END
                WHERE "PlanCode" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId_PlanCode",
                table: "Memberships",
                columns: new[] { "UserId", "PlanCode" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_UserId_PlanCode",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }
    }
}
