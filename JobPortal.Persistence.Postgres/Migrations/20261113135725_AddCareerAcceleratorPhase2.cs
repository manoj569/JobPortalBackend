using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceleratorAssessmentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    SelectionStrategy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAssessmentAttempts", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorAssessmentAttempt_Number", "\"AttemptNumber\" >= 1");
                    table.ForeignKey(
                        name: "FK_AcceleratorAssessmentAttempts_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorAssessmentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SelectionStrategy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaximumQuestions = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAssessmentTemplates", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorAssessmentTemplate_Limits", "\"TimeLimitMinutes\" BETWEEN 1 AND 240 AND \"MaximumQuestions\" BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorAssessmentTemplates_AcceleratorCareerRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AcceleratorCareerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnswerKeyJson = table.Column<string>(type: "jsonb", nullable: false),
                    RubricJson = table.Column<string>(type: "jsonb", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorQuestions", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorQuestion_Bounds", "\"Difficulty\" BETWEEN 1 AND 5 AND \"Weight\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorQuestions_AcceleratorCompetencies_CompetencyId_T~",
                        columns: x => new { x.CompetencyId, x.TaxonomyId },
                        principalTable: "AcceleratorCompetencies",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorRecommendations", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorRecommendation_Priority", "\"Priority\" BETWEEN 1 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorRoadmapEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorRoadmapEdges", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorRoadmapEdge_NotSelf", "\"FromNodeId\" <> \"ToNodeId\"");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorRoadmaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GenerationStrategy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedStartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TargetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorRoadmaps", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorRoadmap_Timeline", "\"TargetAtUtc\" > \"PlannedStartAtUtc\" AND \"EstimatedMinutes\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorSkillEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Proficiency = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ScoringVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorSkillEvidence", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorSkillEvidence_Confidence", "\"Confidence\" BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorAssessmentAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAnswerJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: true),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    EvaluationEvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAssessmentAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorAssessmentAnswers_AcceleratorAssessmentAttempts_~",
                        column: x => x.AttemptId,
                        principalTable: "AcceleratorAssessmentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorRoadmapNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    CompletionEvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorRoadmapNodes", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorRoadmapNode_Progress", "\"EstimatedMinutes\" >= 0 AND \"ProgressPercent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorRoadmapNodes_AcceleratorRoadmaps_RoadmapId",
                        column: x => x.RoadmapId,
                        principalTable: "AcceleratorRoadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLearningActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningActivities", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLearningActivity_Effort", "\"EstimatedMinutes\" BETWEEN 1 AND 1440");
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningActivities_AcceleratorRoadmapNodes_Roadm~",
                        column: x => x.RoadmapNodeId,
                        principalTable: "AcceleratorRoadmapNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProgressEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProgressEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorProgressEvents_AcceleratorLearningActivities_Act~",
                        column: x => x.ActivityId,
                        principalTable: "AcceleratorLearningActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAnswers_AttemptId_QuestionId",
                table: "AcceleratorAssessmentAnswers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAnswers_AttemptId_Sequence",
                table: "AcceleratorAssessmentAnswers",
                columns: new[] { "AttemptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAnswers_IsDeleted",
                table: "AcceleratorAssessmentAnswers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_GoalId",
                table: "AcceleratorAssessmentAttempts",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_IsDeleted",
                table: "AcceleratorAssessmentAttempts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_UserId_GoalId_Status",
                table: "AcceleratorAssessmentAttempts",
                columns: new[] { "UserId", "GoalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentAttempts_UserId_RequestKey",
                table: "AcceleratorAssessmentAttempts",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentTemplates_IsDeleted",
                table: "AcceleratorAssessmentTemplates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentTemplates_RoleId_IsActive",
                table: "AcceleratorAssessmentTemplates",
                columns: new[] { "RoleId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAssessmentTemplates_RoleId_Version",
                table: "AcceleratorAssessmentTemplates",
                columns: new[] { "RoleId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningActivities_IsDeleted",
                table: "AcceleratorLearningActivities",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningActivities_RoadmapNodeId",
                table: "AcceleratorLearningActivities",
                column: "RoadmapNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningActivities_UserId_ScheduledAtUtc_Status",
                table: "AcceleratorLearningActivities",
                columns: new[] { "UserId", "ScheduledAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProgressEvents_ActivityId_OccurredAtUtc",
                table: "AcceleratorProgressEvents",
                columns: new[] { "ActivityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProgressEvents_IsDeleted",
                table: "AcceleratorProgressEvents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProgressEvents_UserId_RequestKey",
                table: "AcceleratorProgressEvents",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorQuestions_CompetencyId_TaxonomyId",
                table: "AcceleratorQuestions",
                columns: new[] { "CompetencyId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorQuestions_CompetencyId_Version",
                table: "AcceleratorQuestions",
                columns: new[] { "CompetencyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorQuestions_IsDeleted",
                table: "AcceleratorQuestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorQuestions_TaxonomyId_IsActive_Difficulty",
                table: "AcceleratorQuestions",
                columns: new[] { "TaxonomyId", "IsActive", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRecommendations_IsDeleted",
                table: "AcceleratorRecommendations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRecommendations_UserId_GoalId_GeneratedAtUtc",
                table: "AcceleratorRecommendations",
                columns: new[] { "UserId", "GoalId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapEdges_IsDeleted",
                table: "AcceleratorRoadmapEdges",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapEdges_RoadmapId_FromNodeId_ToNodeId",
                table: "AcceleratorRoadmapEdges",
                columns: new[] { "RoadmapId", "FromNodeId", "ToNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapEdges_RoadmapId_ToNodeId",
                table: "AcceleratorRoadmapEdges",
                columns: new[] { "RoadmapId", "ToNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapNodes_IsDeleted",
                table: "AcceleratorRoadmapNodes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapNodes_RoadmapId_Sequence",
                table: "AcceleratorRoadmapNodes",
                columns: new[] { "RoadmapId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmapNodes_RoadmapId_StableKey",
                table: "AcceleratorRoadmapNodes",
                columns: new[] { "RoadmapId", "StableKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmaps_GoalId_Version",
                table: "AcceleratorRoadmaps",
                columns: new[] { "GoalId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmaps_IsDeleted",
                table: "AcceleratorRoadmaps",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoadmaps_UserId_Status",
                table: "AcceleratorRoadmaps",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_AttemptId",
                table: "AcceleratorSkillEvidence",
                column: "AttemptId",
                unique: true,
                filter: "\"AttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_IsDeleted",
                table: "AcceleratorSkillEvidence",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_UserId_GoalId_CompetencyId_Evaluat~",
                table: "AcceleratorSkillEvidence",
                columns: new[] { "UserId", "GoalId", "CompetencyId", "EvaluatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceleratorAssessmentAnswers");

            migrationBuilder.DropTable(
                name: "AcceleratorAssessmentTemplates");

            migrationBuilder.DropTable(
                name: "AcceleratorProgressEvents");

            migrationBuilder.DropTable(
                name: "AcceleratorQuestions");

            migrationBuilder.DropTable(
                name: "AcceleratorRecommendations");

            migrationBuilder.DropTable(
                name: "AcceleratorRoadmapEdges");

            migrationBuilder.DropTable(
                name: "AcceleratorSkillEvidence");

            migrationBuilder.DropTable(
                name: "AcceleratorAssessmentAttempts");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningActivities");

            migrationBuilder.DropTable(
                name: "AcceleratorRoadmapNodes");

            migrationBuilder.DropTable(
                name: "AcceleratorRoadmaps");
        }
    }
}
