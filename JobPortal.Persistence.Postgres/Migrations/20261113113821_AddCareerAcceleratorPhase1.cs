using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceleratorBillingCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorBillingCycles", x => x.Id);
                    table.UniqueConstraint("AK_AcceleratorBillingCycles_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_AcceleratorCycle_Bounds", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AcceleratorBillingCycles_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorBillingCycles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperienceYears = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    EducationSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    WeeklyStudyMinutes = table.Column<int>(type: "integer", nullable: false),
                    LinkedResumeUploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProfiles", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProfile_Experience", "\"ExperienceYears\" BETWEEN 0 AND 60");
                    table.CheckConstraint("CK_AcceleratorProfile_Study", "\"WeeklyStudyMinutes\" BETWEEN 15 AND 10080");
                    table.ForeignKey(
                        name: "FK_AcceleratorProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorTaxonomies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorTaxonomies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorAllowances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<int>(type: "integer", nullable: false),
                    Allocated = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAllowances", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorAllowance_Bounds", "\"Allocated\" BETWEEN 1 AND 10000 AND \"Feature\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_AcceleratorAllowances_AcceleratorBillingCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "AcceleratorBillingCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorUsageReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Feature = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BudgetDayUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReservedCostInr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ActualCostInr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    ReservedInputTokens = table.Column<int>(type: "integer", nullable: false),
                    ReservedOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    PolicyViolation = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorUsageReservations", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorUsage_Cost", "\"ReservedCostInr\" >= 0 AND (\"ActualCostInr\" IS NULL OR \"ActualCostInr\" >= 0)");
                    table.CheckConstraint("CK_AcceleratorUsage_Lease", "\"LeaseExpiresAtUtc\" > \"ReservedAtUtc\"");
                    table.CheckConstraint("CK_AcceleratorUsage_State", "\"State\" BETWEEN 1 AND 5 AND \"Feature\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_AcceleratorUsage_Tokens", "\"InputTokens\" >= 0 AND \"OutputTokens\" >= 0 AND \"ReservedInputTokens\" >= 0 AND \"ReservedOutputTokens\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorUsageReservations_AcceleratorBillingCycles_Cycle~",
                        columns: x => new { x.CycleId, x.UserId },
                        principalTable: "AcceleratorBillingCycles",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProfileSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelfReportedProficiency = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProfileSkills", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProfileSkill_Proficiency", "\"SelfReportedProficiency\" BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_AcceleratorProfileSkills_AcceleratorProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "AcceleratorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProfileSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorCareerRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorCareerRoles", x => x.Id);
                    table.UniqueConstraint("AK_AcceleratorCareerRoles_Id_TaxonomyId", x => new { x.Id, x.TaxonomyId });
                    table.ForeignKey(
                        name: "FK_AcceleratorCareerRoles_AcceleratorTaxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "AcceleratorTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorCompetencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillNameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssessmentMethodsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResourcesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorCompetencies", x => x.Id);
                    table.UniqueConstraint("AK_AcceleratorCompetencies_Id_TaxonomyId", x => new { x.Id, x.TaxonomyId });
                    table.CheckConstraint("CK_AcceleratorCompetency_Difficulty", "\"Difficulty\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_AcceleratorCompetencies_AcceleratorCompetencies_ParentId_Ta~",
                        columns: x => new { x.ParentId, x.TaxonomyId },
                        principalTable: "AcceleratorCompetencies",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCompetencies_AcceleratorTaxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "AcceleratorTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCompetencies_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorUsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CostInr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorUsageEvents", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorUsageEvent_State", "\"State\" BETWEEN 1 AND 5 AND \"CostInr\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorUsageEvents_AcceleratorUsageReservations_Reserva~",
                        column: x => x.ReservationId,
                        principalTable: "AcceleratorUsageReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorCareerGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    WeeklyStudyMinutes = table.Column<int>(type: "integer", nullable: false),
                    TargetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorCareerGoals", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorGoal_Status", "\"Status\" BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_AcceleratorGoal_Study", "\"WeeklyStudyMinutes\" BETWEEN 15 AND 10080");
                    table.ForeignKey(
                        name: "FK_AcceleratorCareerGoals_AcceleratorCareerRoles_RoleId_Taxono~",
                        columns: x => new { x.RoleId, x.TaxonomyId },
                        principalTable: "AcceleratorCareerRoles",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCareerGoals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPrerequisites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredCompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPrerequisites", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorPrerequisite_NotSelf", "\"CompetencyId\" <> \"RequiredCompetencyId\"");
                    table.ForeignKey(
                        name: "FK_AcceleratorPrerequisites_AcceleratorCompetencies_Competency~",
                        columns: x => new { x.CompetencyId, x.TaxonomyId },
                        principalTable: "AcceleratorCompetencies",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorPrerequisites_AcceleratorCompetencies_RequiredCo~",
                        columns: x => new { x.RequiredCompetencyId, x.TaxonomyId },
                        principalTable: "AcceleratorCompetencies",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorRoleRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedProficiency = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorRoleRequirements", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorRequirement_Proficiency", "\"ExpectedProficiency\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_AcceleratorRoleRequirements_AcceleratorCareerRoles_RoleId_T~",
                        columns: x => new { x.RoleId, x.TaxonomyId },
                        principalTable: "AcceleratorCareerRoles",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorRoleRequirements_AcceleratorCompetencies_Compete~",
                        columns: x => new { x.CompetencyId, x.TaxonomyId },
                        principalTable: "AcceleratorCompetencies",
                        principalColumns: new[] { "Id", "TaxonomyId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAllowances_CycleId_Feature",
                table: "AcceleratorAllowances",
                columns: new[] { "CycleId", "Feature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAllowances_IsDeleted",
                table: "AcceleratorAllowances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorBillingCycles_IsDeleted",
                table: "AcceleratorBillingCycles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorBillingCycles_PaymentId",
                table: "AcceleratorBillingCycles",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorBillingCycles_UserId_StartsAtUtc_EndsAtUtc",
                table: "AcceleratorBillingCycles",
                columns: new[] { "UserId", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerGoals_IsDeleted",
                table: "AcceleratorCareerGoals",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerGoals_RoleId_TaxonomyId",
                table: "AcceleratorCareerGoals",
                columns: new[] { "RoleId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerGoals_UserId_CreatedAtUtc_Id",
                table: "AcceleratorCareerGoals",
                columns: new[] { "UserId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerGoals_UserId_RequestKey",
                table: "AcceleratorCareerGoals",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerRoles_IsDeleted",
                table: "AcceleratorCareerRoles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCareerRoles_TaxonomyId_Code",
                table: "AcceleratorCareerRoles",
                columns: new[] { "TaxonomyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCompetencies_IsDeleted",
                table: "AcceleratorCompetencies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCompetencies_ParentId_TaxonomyId",
                table: "AcceleratorCompetencies",
                columns: new[] { "ParentId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCompetencies_SkillId",
                table: "AcceleratorCompetencies",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCompetencies_TaxonomyId_SkillId",
                table: "AcceleratorCompetencies",
                columns: new[] { "TaxonomyId", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPrerequisites_CompetencyId_RequiredCompetencyId",
                table: "AcceleratorPrerequisites",
                columns: new[] { "CompetencyId", "RequiredCompetencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPrerequisites_CompetencyId_TaxonomyId",
                table: "AcceleratorPrerequisites",
                columns: new[] { "CompetencyId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPrerequisites_IsDeleted",
                table: "AcceleratorPrerequisites",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPrerequisites_RequiredCompetencyId_TaxonomyId",
                table: "AcceleratorPrerequisites",
                columns: new[] { "RequiredCompetencyId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProfiles_IsDeleted",
                table: "AcceleratorProfiles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProfiles_UserId",
                table: "AcceleratorProfiles",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProfileSkills_IsDeleted",
                table: "AcceleratorProfileSkills",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProfileSkills_ProfileId_SkillId",
                table: "AcceleratorProfileSkills",
                columns: new[] { "ProfileId", "SkillId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProfileSkills_SkillId",
                table: "AcceleratorProfileSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoleRequirements_CompetencyId_TaxonomyId",
                table: "AcceleratorRoleRequirements",
                columns: new[] { "CompetencyId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoleRequirements_IsDeleted",
                table: "AcceleratorRoleRequirements",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoleRequirements_RoleId_CompetencyId",
                table: "AcceleratorRoleRequirements",
                columns: new[] { "RoleId", "CompetencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorRoleRequirements_RoleId_TaxonomyId",
                table: "AcceleratorRoleRequirements",
                columns: new[] { "RoleId", "TaxonomyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTaxonomies_IsDeleted",
                table: "AcceleratorTaxonomies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTaxonomies_PublishedAtUtc",
                table: "AcceleratorTaxonomies",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTaxonomies_Version",
                table: "AcceleratorTaxonomies",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageEvents_IsDeleted",
                table: "AcceleratorUsageEvents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageEvents_OccurredAtUtc",
                table: "AcceleratorUsageEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageEvents_ReservationId_State",
                table: "AcceleratorUsageEvents",
                columns: new[] { "ReservationId", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_BudgetDayUtc_State",
                table: "AcceleratorUsageReservations",
                columns: new[] { "BudgetDayUtc", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_CycleId_Feature_State",
                table: "AcceleratorUsageReservations",
                columns: new[] { "CycleId", "Feature", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_CycleId_UserId",
                table: "AcceleratorUsageReservations",
                columns: new[] { "CycleId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_IsDeleted",
                table: "AcceleratorUsageReservations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_State_LeaseExpiresAtUtc",
                table: "AcceleratorUsageReservations",
                columns: new[] { "State", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_UserId_RequestKey",
                table: "AcceleratorUsageReservations",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorUsageReservations_UserId_ReservedAtUtc",
                table: "AcceleratorUsageReservations",
                columns: new[] { "UserId", "ReservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceleratorAllowances");

            migrationBuilder.DropTable(
                name: "AcceleratorCareerGoals");

            migrationBuilder.DropTable(
                name: "AcceleratorPrerequisites");

            migrationBuilder.DropTable(
                name: "AcceleratorProfileSkills");

            migrationBuilder.DropTable(
                name: "AcceleratorRoleRequirements");

            migrationBuilder.DropTable(
                name: "AcceleratorUsageEvents");

            migrationBuilder.DropTable(
                name: "AcceleratorProfiles");

            migrationBuilder.DropTable(
                name: "AcceleratorCareerRoles");

            migrationBuilder.DropTable(
                name: "AcceleratorCompetencies");

            migrationBuilder.DropTable(
                name: "AcceleratorUsageReservations");

            migrationBuilder.DropTable(
                name: "AcceleratorTaxonomies");

            migrationBuilder.DropTable(
                name: "AcceleratorBillingCycles");
        }
    }
}
