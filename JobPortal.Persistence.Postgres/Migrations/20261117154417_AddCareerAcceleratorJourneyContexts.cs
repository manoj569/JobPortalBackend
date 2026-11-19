using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorJourneyContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorRoadmaps",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorRoadmaps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorRecommendations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorRecommendations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "AcceleratorAssessmentTemplates",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TaxonomyId",
                table: "AcceleratorAssessmentTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorAssessmentAttempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorAssessmentAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcceleratorJourneyContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    CareerGoalId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathEnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorJourneyContexts", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorJourneyContext_Scope", "(\"ScopeType\" = 1 AND \"CareerGoalId\" IS NOT NULL AND \"LearningPathEnrollmentId\" IS NULL) OR (\"ScopeType\" = 2 AND \"CareerGoalId\" IS NULL AND \"LearningPathEnrollmentId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyContexts_AcceleratorCareerGoals_CareerGoa~",
                        column: x => x.CareerGoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyContexts_AcceleratorPathEnrollments_Learn~",
                        column: x => x.LearningPathEnrollmentId,
                        principalTable: "AcceleratorPathEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyContexts_AcceleratorTaxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "AcceleratorTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLearningPathAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningPathAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPathAssessments_AcceleratorAssessmentTem~",
                        column: x => x.AssessmentTemplateId,
                        principalTable: "AcceleratorAssessmentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPathAssessments_AcceleratorLearningPaths~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLearningPathCompetencyRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetProficiency = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningPathCompetencyRequirements", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLearningPathCompetencyRequirement_Bounds", "\"TargetProficiency\" BETWEEN 1 AND 5 AND \"Weight\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPathCompetencyRequirements_AcceleratorCo~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPathCompetencyRequirements_AcceleratorLe~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_JourneyContextId",
                table: "AcceleratorSkillEvidence",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_UserId_JourneyContextId",
                table: "AcceleratorSkillEvidence",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmaps_JourneyContextId_Version",
                table: "AcceleratorRoadmaps",
                columns: new[] { "JourneyContextId", "Version" },
                unique: true,
                filter: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmaps_UserId_JourneyContextId",
                table: "AcceleratorRoadmaps",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRecommendations_JourneyContextId",
                table: "AcceleratorRecommendations",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRecommendations_UserId_JourneyContextId",
                table: "AcceleratorRecommendations",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentTemplates_TaxonomyId",
                table: "AcceleratorAssessmentTemplates",
                column: "TaxonomyId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorAssessmentTemplate_Applicability",
                table: "AcceleratorAssessmentTemplates",
                sql: "\"RoleId\" IS NOT NULL OR \"TaxonomyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_JourneyContextId",
                table: "AcceleratorAssessmentAttempts",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_UserId_JourneyContextId",
                table: "AcceleratorAssessmentAttempts",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyContexts_CareerGoalId",
                table: "AcceleratorJourneyContexts",
                column: "CareerGoalId",
                unique: true,
                filter: "\"CareerGoalId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyContexts_IsDeleted",
                table: "AcceleratorJourneyContexts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyContexts_LearningPathEnrollmentId",
                table: "AcceleratorJourneyContexts",
                column: "LearningPathEnrollmentId",
                unique: true,
                filter: "\"LearningPathEnrollmentId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyContexts_TaxonomyId",
                table: "AcceleratorJourneyContexts",
                column: "TaxonomyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyContexts_UserId_ScopeType",
                table: "AcceleratorJourneyContexts",
                columns: new[] { "UserId", "ScopeType" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathAssessments_AssessmentTemplateId",
                table: "AcceleratorLearningPathAssessments",
                column: "AssessmentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathAssessments_IsDeleted",
                table: "AcceleratorLearningPathAssessments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathAssessments_LearningPathId_Purpose",
                table: "AcceleratorLearningPathAssessments",
                columns: new[] { "LearningPathId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathCompetencyRequirements_CompetencyId",
                table: "AcceleratorLearningPathCompetencyRequirements",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathCompetencyRequirements_IsDeleted",
                table: "AcceleratorLearningPathCompetencyRequirements",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPathCompetencyRequirements_LearningPathI~",
                table: "AcceleratorLearningPathCompetencyRequirements",
                columns: new[] { "LearningPathId", "CompetencyId" },
                unique: true);

            // Reuse each CareerGoal UUID as its context UUID. This is deterministic and
            // requires no PostgreSQL UUID extension or application startup services.
            migrationBuilder.Sql("""
                INSERT INTO "AcceleratorJourneyContexts"
                    ("Id", "UserId", "ScopeType", "CareerGoalId", "LearningPathEnrollmentId", "TaxonomyId", "Revision", "CreatedAtUtc", "IsDeleted")
                SELECT g."Id", g."UserId", 1, g."Id", NULL, g."TaxonomyId", g."Id", g."CreatedAtUtc", false
                FROM "AcceleratorCareerGoals" g
                WHERE NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c WHERE c."CareerGoalId" = g."Id" AND NOT c."IsDeleted");

                UPDATE "AcceleratorAssessmentAttempts" a SET "JourneyContextId" = c."Id"
                FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
                UPDATE "AcceleratorSkillEvidence" a SET "JourneyContextId" = c."Id"
                FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
                UPDATE "AcceleratorRoadmaps" a SET "JourneyContextId" = c."Id"
                FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
                UPDATE "AcceleratorRecommendations" a SET "JourneyContextId" = c."Id"
                FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorAssessmentAttempts_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorAssessmentAttempts",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorAssessmentTemplates_AcceleratorTaxonomies_Taxono~",
                table: "AcceleratorAssessmentTemplates",
                column: "TaxonomyId",
                principalTable: "AcceleratorTaxonomies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorRecommendations_AcceleratorJourneyContexts_Journ~",
                table: "AcceleratorRecommendations",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorRoadmaps_AcceleratorJourneyContexts_JourneyConte~",
                table: "AcceleratorRoadmaps",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorJourneyContexts_Journey~",
                table: "AcceleratorSkillEvidence",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Generic journeys cannot be converted to CareerGoals without fabricating
            // ownership. Refuse rollback once such records exist instead of inserting zero UUIDs.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "AcceleratorAssessmentAttempts" WHERE "GoalId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "AcceleratorSkillEvidence" WHERE "GoalId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "AcceleratorRoadmaps" WHERE "GoalId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "AcceleratorRecommendations" WHERE "GoalId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "AcceleratorAssessmentTemplates" WHERE "RoleId" IS NULL)
                  THEN RAISE EXCEPTION 'Cannot roll back JourneyContexts while generic journey records exist.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorAssessmentAttempts_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorAssessmentAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorAssessmentTemplates_AcceleratorTaxonomies_Taxono~",
                table: "AcceleratorAssessmentTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorRecommendations_AcceleratorJourneyContexts_Journ~",
                table: "AcceleratorRecommendations");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorRoadmaps_AcceleratorJourneyContexts_JourneyConte~",
                table: "AcceleratorRoadmaps");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorJourneyContexts_Journey~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropTable(
                name: "AcceleratorJourneyContexts");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningPathAssessments");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningPathCompetencyRequirements");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_JourneyContextId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_UserId_JourneyContextId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorRoadmaps_JourneyContextId_Version",
                table: "AcceleratorRoadmaps");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorRoadmaps_UserId_JourneyContextId",
                table: "AcceleratorRoadmaps");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorRecommendations_JourneyContextId",
                table: "AcceleratorRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorRecommendations_UserId_JourneyContextId",
                table: "AcceleratorRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorAssessmentTemplates_TaxonomyId",
                table: "AcceleratorAssessmentTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorAssessmentTemplate_Applicability",
                table: "AcceleratorAssessmentTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorAssessmentAttempts_JourneyContextId",
                table: "AcceleratorAssessmentAttempts");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorAssessmentAttempts_UserId_JourneyContextId",
                table: "AcceleratorAssessmentAttempts");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorRoadmaps");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorRecommendations");

            migrationBuilder.DropColumn(
                name: "TaxonomyId",
                table: "AcceleratorAssessmentTemplates");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorAssessmentAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorRoadmaps",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorRecommendations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "AcceleratorAssessmentTemplates",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorAssessmentAttempts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
