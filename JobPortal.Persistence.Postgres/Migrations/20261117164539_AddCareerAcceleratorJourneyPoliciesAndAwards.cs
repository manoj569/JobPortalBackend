using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorJourneyPoliciesAndAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceleratorAchievementDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Rule = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAchievementDefinitions", x => x.Id);
                    table.CheckConstraint("CK_AchievementDefinition_Threshold", "\"Threshold\" BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorSkillProficiencyPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ComponentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MinimumEvidenceConfidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    CertificateMinimumAssessments = table.Column<int>(type: "integer", nullable: false),
                    CertificateMinimumScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CertificateRequiresModules = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorSkillProficiencyPolicies", x => x.Id);
                    table.CheckConstraint("CK_ProficiencyPolicy_Bounds", "\"Version\" > 0 AND \"MinimumEvidenceConfidence\" BETWEEN 0 AND 1 AND \"CertificateMinimumAssessments\" > 0 AND \"CertificateMinimumScore\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorSkillProficiencyPolicies_AcceleratorLearningPath~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateAcceleratorAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JourneyContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateAcceleratorAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateAcceleratorAchievements_AcceleratorAchievementDefi~",
                        column: x => x.AchievementDefinitionId,
                        principalTable: "AcceleratorAchievementDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateAcceleratorAchievements_AcceleratorJourneyContexts~",
                        column: x => x.JourneyContextId,
                        principalTable: "AcceleratorJourneyContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateAcceleratorAchievements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorEnrollmentCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JourneyContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CertificateIdentifier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ArtifactStorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorEnrollmentCertificates", x => x.Id);
                    table.CheckConstraint("CK_Certificate_Artifact", "\"State\" <> 4 OR (\"IssuedAtUtc\" IS NOT NULL AND \"CertificateIdentifier\" IS NOT NULL AND \"ArtifactStorageKey\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AcceleratorEnrollmentCertificates_AcceleratorJourneyContext~",
                        column: x => x.JourneyContextId,
                        principalTable: "AcceleratorJourneyContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorEnrollmentCertificates_AcceleratorSkillProficien~",
                        column: x => x.PolicyId,
                        principalTable: "AcceleratorSkillProficiencyPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorEnrollmentCertificates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProficiencyBands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MinimumScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProficiencyBands", x => x.Id);
                    table.CheckConstraint("CK_ProficiencyBand_Range", "\"MinimumScore\" >= 0 AND \"MaximumScore\" <= 100 AND \"MinimumScore\" < \"MaximumScore\"");
                    table.ForeignKey(
                        name: "FK_AcceleratorProficiencyBands_AcceleratorSkillProficiencyPoli~",
                        column: x => x.PolicyId,
                        principalTable: "AcceleratorSkillProficiencyPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Adjacent bands are half-open; the final upper bound includes 100.
            // btree_gist is already part of the PostgreSQL migration chain.
            migrationBuilder.Sql("""
                ALTER TABLE "AcceleratorProficiencyBands"
                ADD CONSTRAINT "EX_AcceleratorProficiencyBands_NoOverlap"
                EXCLUDE USING gist (
                  "PolicyId" WITH =,
                  numrange("MinimumScore", "MaximumScore", CASE WHEN "MaximumScore" = 100 THEN '[]' ELSE '[)' END) WITH &&
                ) WHERE ("IsActive" AND NOT "IsDeleted");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAchievementDefinitions_Code",
                table: "AcceleratorAchievementDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAchievementDefinitions_IsDeleted",
                table: "AcceleratorAchievementDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentCertificates_IsDeleted",
                table: "AcceleratorEnrollmentCertificates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentCertificates_JourneyContextId",
                table: "AcceleratorEnrollmentCertificates",
                column: "JourneyContextId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentCertificates_PolicyId",
                table: "AcceleratorEnrollmentCertificates",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentCertificates_UserId_State",
                table: "AcceleratorEnrollmentCertificates",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProficiencyBands_IsDeleted",
                table: "AcceleratorProficiencyBands",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProficiencyBands_PolicyId_MinimumScore",
                table: "AcceleratorProficiencyBands",
                columns: new[] { "PolicyId", "MinimumScore" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillProficiencyPolicies_Code_Version",
                table: "AcceleratorSkillProficiencyPolicies",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillProficiencyPolicies_IsDeleted",
                table: "AcceleratorSkillProficiencyPolicies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillProficiencyPolicies_LearningPathId_IsActive",
                table: "AcceleratorSkillProficiencyPolicies",
                columns: new[] { "LearningPathId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateAcceleratorAchievements_AchievementDefinitionId",
                table: "CandidateAcceleratorAchievements",
                column: "AchievementDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateAcceleratorAchievements_IsDeleted",
                table: "CandidateAcceleratorAchievements",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateAcceleratorAchievements_JourneyContextId_Achieveme~",
                table: "CandidateAcceleratorAchievements",
                columns: new[] { "JourneyContextId", "AchievementDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateAcceleratorAchievements_UserId_AwardedAtUtc",
                table: "CandidateAcceleratorAchievements",
                columns: new[] { "UserId", "AwardedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback must not silently erase earned recognition or issued artifacts.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "CandidateAcceleratorAchievements")
                    OR EXISTS (SELECT 1 FROM "AcceleratorEnrollmentCertificates" WHERE "IssuedAtUtc" IS NOT NULL)
                  THEN RAISE EXCEPTION 'Cannot roll back journey recognition while earned awards or issued certificates exist.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "AcceleratorEnrollmentCertificates");

            migrationBuilder.DropTable(
                name: "AcceleratorProficiencyBands");

            migrationBuilder.DropTable(
                name: "CandidateAcceleratorAchievements");

            migrationBuilder.DropTable(
                name: "AcceleratorSkillProficiencyPolicies");

            migrationBuilder.DropTable(
                name: "AcceleratorAchievementDefinitions");
        }
    }
}
