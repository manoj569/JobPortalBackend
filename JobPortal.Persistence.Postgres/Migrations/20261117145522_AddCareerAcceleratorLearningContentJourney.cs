using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorLearningContentJourney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceleratorLearningPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ShortDescription = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CanonicalCareerRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningPaths", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLearningPath_Bounds", "\"Difficulty\" BETWEEN 1 AND 5 AND \"EstimatedMinutes\" >= 0");
                    table.CheckConstraint("CK_AcceleratorLearningPath_CanonicalReference", "(\"Type\" = 1 AND \"CanonicalCareerRoleId\" IS NOT NULL AND \"CanonicalSkillId\" IS NULL) OR (\"Type\" = 2 AND \"CanonicalSkillId\" IS NOT NULL AND \"CanonicalCareerRoleId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPaths_AcceleratorCareerRoles_CanonicalCa~",
                        column: x => x.CanonicalCareerRoleId,
                        principalTable: "AcceleratorCareerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningPaths_Skills_CanonicalSkillId",
                        column: x => x.CanonicalSkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLearningModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssessmentTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InterviewVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningModules", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLearningModule_Bounds", "\"Sequence\" >= 1 AND \"Difficulty\" BETWEEN 1 AND 5 AND \"EstimatedMinutes\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModules_AcceleratorAssessmentTemplates_A~",
                        column: x => x.AssessmentTemplateId,
                        principalTable: "AcceleratorAssessmentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModules_AcceleratorInterviewVersions_Int~",
                        column: x => x.InterviewVersionId,
                        principalTable: "AcceleratorInterviewVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModules_AcceleratorLearningPaths_Learnin~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModules_AcceleratorProjectVersions_Proje~",
                        column: x => x.ProjectVersionId,
                        principalTable: "AcceleratorProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLearningModulePrerequisites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLearningModulePrerequisites", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLearningModulePrerequisite_NotSelf", "\"ModuleId\" <> \"RequiredModuleId\"");
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearningM~",
                        column: x => x.ModuleId,
                        principalTable: "AcceleratorLearningModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearningP~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearning~1",
                        column: x => x.RequiredModuleId,
                        principalTable: "AcceleratorLearningModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLessons", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorLesson_Bounds", "\"Sequence\" >= 1 AND \"EstimatedMinutes\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorLessons_AcceleratorLearningModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "AcceleratorLearningModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLessonResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLessonResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorLessonResources_AcceleratorLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "AcceleratorLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPathEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentLevel = table.Column<int>(type: "integer", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetLevel = table.Column<int>(type: "integer", nullable: false),
                    WeeklyCommitmentMinutes = table.Column<int>(type: "integer", nullable: false),
                    TargetCompletionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnrolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentModuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentLessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OverallProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPathEnrollments", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorPathEnrollment_Bounds", "\"CurrentLevel\" BETWEEN 0 AND 5 AND \"TargetLevel\" BETWEEN 1 AND 5 AND \"WeeklyCommitmentMinutes\" BETWEEN 1 AND 10080 AND \"OverallProgressPercent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorPathEnrollments_AcceleratorLearningModules_Curre~",
                        column: x => x.CurrentModuleId,
                        principalTable: "AcceleratorLearningModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorPathEnrollments_AcceleratorLearningPaths_Learnin~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorPathEnrollments_AcceleratorLessons_CurrentLesson~",
                        column: x => x.CurrentLessonId,
                        principalTable: "AcceleratorLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorEnrollmentProgressEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorEnrollmentProgressEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorEnrollmentProgressEvents_AcceleratorLessons_Less~",
                        column: x => x.LessonId,
                        principalTable: "AcceleratorLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorEnrollmentProgressEvents_AcceleratorPathEnrollme~",
                        column: x => x.EnrollmentId,
                        principalTable: "AcceleratorPathEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorLessonProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRequestKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorLessonProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorLessonProgress_AcceleratorLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "AcceleratorLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorLessonProgress_AcceleratorPathEnrollments_Enroll~",
                        column: x => x.EnrollmentId,
                        principalTable: "AcceleratorPathEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentProgressEvents_EnrollmentId_OccurredAt~",
                table: "AcceleratorEnrollmentProgressEvents",
                columns: new[] { "EnrollmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentProgressEvents_EnrollmentId_RequestKey",
                table: "AcceleratorEnrollmentProgressEvents",
                columns: new[] { "EnrollmentId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentProgressEvents_IsDeleted",
                table: "AcceleratorEnrollmentProgressEvents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorEnrollmentProgressEvents_LessonId",
                table: "AcceleratorEnrollmentProgressEvents",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModulePrerequisites_IsDeleted",
                table: "AcceleratorLearningModulePrerequisites",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModulePrerequisites_LearningPathId_Requi~",
                table: "AcceleratorLearningModulePrerequisites",
                columns: new[] { "LearningPathId", "RequiredModuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModulePrerequisites_ModuleId_RequiredMod~",
                table: "AcceleratorLearningModulePrerequisites",
                columns: new[] { "ModuleId", "RequiredModuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModulePrerequisites_RequiredModuleId",
                table: "AcceleratorLearningModulePrerequisites",
                column: "RequiredModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_AssessmentTemplateId",
                table: "AcceleratorLearningModules",
                column: "AssessmentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_InterviewVersionId",
                table: "AcceleratorLearningModules",
                column: "InterviewVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_IsDeleted",
                table: "AcceleratorLearningModules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_LearningPathId_Sequence",
                table: "AcceleratorLearningModules",
                columns: new[] { "LearningPathId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_LearningPathId_Status",
                table: "AcceleratorLearningModules",
                columns: new[] { "LearningPathId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningModules_ProjectVersionId",
                table: "AcceleratorLearningModules",
                column: "ProjectVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPaths_CanonicalCareerRoleId",
                table: "AcceleratorLearningPaths",
                column: "CanonicalCareerRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPaths_CanonicalSkillId",
                table: "AcceleratorLearningPaths",
                column: "CanonicalSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPaths_IsDeleted",
                table: "AcceleratorLearningPaths",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPaths_Slug",
                table: "AcceleratorLearningPaths",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLearningPaths_Type_Status_DisplayOrder",
                table: "AcceleratorLearningPaths",
                columns: new[] { "Type", "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonProgress_EnrollmentId_LessonId",
                table: "AcceleratorLessonProgress",
                columns: new[] { "EnrollmentId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonProgress_EnrollmentId_Status",
                table: "AcceleratorLessonProgress",
                columns: new[] { "EnrollmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonProgress_IsDeleted",
                table: "AcceleratorLessonProgress",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonProgress_LessonId",
                table: "AcceleratorLessonProgress",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonResources_IsDeleted",
                table: "AcceleratorLessonResources",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessonResources_LessonId_Sequence",
                table: "AcceleratorLessonResources",
                columns: new[] { "LessonId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessons_IsDeleted",
                table: "AcceleratorLessons",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessons_ModuleId_Sequence",
                table: "AcceleratorLessons",
                columns: new[] { "ModuleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorLessons_ModuleId_Status",
                table: "AcceleratorLessons",
                columns: new[] { "ModuleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_CurrentLessonId",
                table: "AcceleratorPathEnrollments",
                column: "CurrentLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_CurrentModuleId",
                table: "AcceleratorPathEnrollments",
                column: "CurrentModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_IsDeleted",
                table: "AcceleratorPathEnrollments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_LearningPathId",
                table: "AcceleratorPathEnrollments",
                column: "LearningPathId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_UserId_LearningPathId",
                table: "AcceleratorPathEnrollments",
                columns: new[] { "UserId", "LearningPathId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (1, 2, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_UserId_RequestKey",
                table: "AcceleratorPathEnrollments",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPathEnrollments_UserId_Status_LastActivityAtUtc",
                table: "AcceleratorPathEnrollments",
                columns: new[] { "UserId", "Status", "LastActivityAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceleratorEnrollmentProgressEvents");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningModulePrerequisites");

            migrationBuilder.DropTable(
                name: "AcceleratorLessonProgress");

            migrationBuilder.DropTable(
                name: "AcceleratorLessonResources");

            migrationBuilder.DropTable(
                name: "AcceleratorPathEnrollments");

            migrationBuilder.DropTable(
                name: "AcceleratorLessons");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningModules");

            migrationBuilder.DropTable(
                name: "AcceleratorLearningPaths");
        }
    }
}
