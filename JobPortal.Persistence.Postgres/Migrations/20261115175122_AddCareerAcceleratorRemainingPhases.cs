using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorRemainingPhases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceleratorRefundReviewAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceleratorRefundReviewReason",
                table: "Payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceleratorRefundReviewRequired",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersion",
                table: "Payments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderCheckoutExpiresAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCheckoutUrl",
                table: "Payments",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseRequestKey",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignmentId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CandidateProjectId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InterviewEvaluationId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InterviewSessionId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectEvaluationId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectVersionId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Score",
                table: "AcceleratorSkillEvidence",
                type: "numeric(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionQuestionId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "AcceleratorSkillEvidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TargetRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewDefinitions_AcceleratorCareerRoles_Targ~",
                        column: x => x.TargetRoleId,
                        principalTable: "AcceleratorCareerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPassportShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    EnabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPassportShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorPassportShares_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPracticeEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Classification = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StrengthsJson = table.Column<string>(type: "jsonb", nullable: false),
                    WeaknessesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    SuggestedImprovement = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    RubricVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPracticeEvaluations", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorPracticeEvaluation_Score", "\"Score\" >= 0 AND \"MaximumScore\" > 0 AND \"Score\" <= \"MaximumScore\" AND \"Confidence\" BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    QuestionType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GenerationStrategy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPracticeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorPracticeSessions_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorPracticeSessions_AcceleratorRoadmapNodes_Roadmap~",
                        column: x => x.RoadmapNodeId,
                        principalTable: "AcceleratorRoadmapNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorTutorContextSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    TokenEstimate = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorTutorContextSnapshots", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorTutorContextSnapshot_Tokens", "\"TokenEstimate\" BETWEEN 0 AND 32000");
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorTutorConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorTutorConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorTutorConversations_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorTutorConversations_AcceleratorCompetencies_Compe~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorTutorConversations_AcceleratorRoadmapNodes_Roadm~",
                        column: x => x.RoadmapNodeId,
                        principalTable: "AcceleratorRoadmapNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorTutorMemorySummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsInvalidated = table.Column<bool>(type: "boolean", nullable: false),
                    SourceRange = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorTutorMemorySummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CandidateMailboxConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ProviderAccountIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GrantedScopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TokenReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateMailboxConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateMailboxConnections_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployerContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfessionalEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Verification = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    VerificationEvidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DiscoveryExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerContacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketJobProcessingStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaxonomyRevision = table.Column<long>(type: "bigint", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketJobProcessingStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketJobProcessingStates_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketProcessingCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    LastJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastJobCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    JobsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    Failures = table.Column<long>(type: "bigint", nullable: false),
                    TaxonomyRevision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketProcessingCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceleratorRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRoles_AcceleratorCareerRoles_AcceleratorRoleId",
                        column: x => x.AcceleratorRoleId,
                        principalTable: "AcceleratorCareerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketRoles_MarketRoles_MergedIntoId",
                        column: x => x.MergedIntoId,
                        principalTable: "MarketRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketSkills_AcceleratorCompetencies_CompetencyId",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketSkills_MarketSkills_MergedIntoId",
                        column: x => x.MergedIntoId,
                        principalTable: "MarketSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TalentShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccessCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalentShareLinks_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentShareLinks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentShareLinks_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentShareLinks_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    TargetExperienceLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewVersions", x => x.Id);
                    table.CheckConstraint("CK_InterviewVersion_Bounds", "\"Version\" > 0 AND \"EstimatedMinutes\" > 0 AND \"Status\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewVersions_AcceleratorInterviewDefinition~",
                        column: x => x.DefinitionId,
                        principalTable: "AcceleratorInterviewDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPracticeQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    PublicOptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnswerKeyJson = table.Column<string>(type: "jsonb", nullable: false),
                    RubricJson = table.Column<string>(type: "jsonb", nullable: false),
                    GenerationVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsQualityApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPracticeQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorPracticeQuestions_AcceleratorPracticeSessions_Se~",
                        column: x => x.SessionId,
                        principalTable: "AcceleratorPracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TargetRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    BusinessContext = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CatalogStatus = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectVersions", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectVersion_Bounds", "\"Version\" > 0 AND \"EstimatedMinutes\" > 0 AND \"CatalogStatus\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectVersions_AcceleratorCareerRoles_TargetRol~",
                        column: x => x.TargetRoleId,
                        principalTable: "AcceleratorCareerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectVersions_AcceleratorProjectDefinitions_De~",
                        column: x => x.DefinitionId,
                        principalTable: "AcceleratorProjectDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorTutorMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostInr = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorTutorMessages", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorTutorMessage_Tokens", "\"InputTokens\" >= 0 AND \"OutputTokens\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorTutorMessages_AcceleratorTutorConversations_Conv~",
                        column: x => x.ConversationId,
                        principalTable: "AcceleratorTutorConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketJobRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketJobRoles", x => x.Id);
                    table.CheckConstraint("CK_MarketJobRole_Confidence", "\"Confidence\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_MarketJobRoles_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketJobRoles_MarketRoles_MarketRoleId",
                        column: x => x.MarketRoleId,
                        principalTable: "MarketRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketRoleAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedText = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRoleAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRoleAliases_MarketRoles_MarketRoleId",
                        column: x => x.MarketRoleId,
                        principalTable: "MarketRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketRoleDiscoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    RawTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedMarketRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRoleDiscoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketRoleDiscoveries_MarketRoles_ResolvedMarketRoleId",
                        column: x => x.ResolvedMarketRoleId,
                        principalTable: "MarketRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketJobSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketSkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    FirstObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketJobSkills", x => x.Id);
                    table.CheckConstraint("CK_MarketJobSkill_Confidence", "\"Confidence\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_MarketJobSkills_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketJobSkills_MarketSkills_MarketSkillId",
                        column: x => x.MarketSkillId,
                        principalTable: "MarketSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketSkillAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketSkillId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedText = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSkillAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketSkillAliases_MarketSkills_MarketSkillId",
                        column: x => x.MarketSkillId,
                        principalTable: "MarketSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketSkillDiscoveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    SuggestedSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawTerm = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NormalizedTerm = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SuggestedCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedMarketSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSkillDiscoveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketSkillDiscoveries_MarketSkills_ResolvedMarketSkillId",
                        column: x => x.ResolvedMarketSkillId,
                        principalTable: "MarketSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketSkillDiscoveries_Skills_SuggestedSkillId",
                        column: x => x.SuggestedSkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TalentOutreachDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    MailboxConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsageReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SendIdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureEvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DispatchStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentOutreachDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_AcceleratorUsageReservations_UsageRese~",
                        column: x => x.UsageReservationId,
                        principalTable: "AcceleratorUsageReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_CandidateMailboxConnections_MailboxCon~",
                        column: x => x.MailboxConnectionId,
                        principalTable: "CandidateMailboxConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_TalentShareLinks_ShareLinkId",
                        column: x => x.ShareLinkId,
                        principalTable: "TalentShareLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachDrafts_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TalentSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ViewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InterviewMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalentSubmissions_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentSubmissions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentSubmissions_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentSubmissions_TalentShareLinks_ShareLinkId",
                        column: x => x.ShareLinkId,
                        principalTable: "TalentShareLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentSubmissions_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewCompetencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedProficiency = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    QuestionCoverageTarget = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewCompetencies", x => x.Id);
                    table.CheckConstraint("CK_InterviewCompetency_Bounds", "\"ExpectedProficiency\" BETWEEN 0 AND 4 AND \"Weight\" > 0 AND \"QuestionCoverageTarget\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewCompetencies_AcceleratorCompetencies_Co~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewCompetencies_AcceleratorInterviewVersio~",
                        column: x => x.InterviewVersionId,
                        principalTable: "AcceleratorInterviewVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewRubricCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewRubricCriteria", x => x.Id);
                    table.CheckConstraint("CK_InterviewCriterion_Bounds", "\"Sequence\" > 0 AND \"Weight\" > 0 AND \"MaximumScore\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewRubricCriteria_AcceleratorCompetencies_~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewRubricCriteria_AcceleratorInterviewVers~",
                        column: x => x.InterviewVersionId,
                        principalTable: "AcceleratorInterviewVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    MaxFollowUpsPerQuestion = table.Column<int>(type: "integer", nullable: false),
                    AllowStaticFallback = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewSections", x => x.Id);
                    table.CheckConstraint("CK_InterviewSection_Bounds", "\"Sequence\" > 0 AND \"EstimatedMinutes\" > 0 AND \"QuestionCount\" BETWEEN 1 AND 50 AND \"MaxFollowUpsPerQuestion\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewSections_AcceleratorInterviewVersions_I~",
                        column: x => x.InterviewVersionId,
                        principalTable: "AcceleratorInterviewVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OverallScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    ReadinessImpact = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewSessions", x => x.Id);
                    table.CheckConstraint("CK_InterviewSession_Score", "\"OverallScore\" IS NULL OR (\"OverallScore\" BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewSessions_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewSessions_AcceleratorInterviewVersions_I~",
                        column: x => x.InterviewVersionId,
                        principalTable: "AcceleratorInterviewVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorPracticeAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerJson = table.Column<string>(type: "jsonb", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorPracticeAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorPracticeAttempts_AcceleratorPracticeQuestions_Qu~",
                        column: x => x.QuestionId,
                        principalTable: "AcceleratorPracticeQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorPracticeAttempts_AcceleratorPracticeSessions_Ses~",
                        column: x => x.SessionId,
                        principalTable: "AcceleratorPracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectCompetencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrerequisite = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredProficiency = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectCompetencies", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectCompetency_Proficiency", "\"RequiredProficiency\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectCompetencies_AcceleratorCompetencies_Comp~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectCompetencies_AcceleratorProjectVersions_P~",
                        column: x => x.ProjectVersionId,
                        principalTable: "AcceleratorProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectMilestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectMilestones", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectMilestone_Bounds", "\"Sequence\" > 0 AND \"EstimatedMinutes\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectMilestones_AcceleratorProjectVersions_Pro~",
                        column: x => x.ProjectVersionId,
                        principalTable: "AcceleratorProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectRubrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectRubrics", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectRubric_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectRubrics_AcceleratorProjectVersions_Projec~",
                        column: x => x.ProjectVersionId,
                        principalTable: "AcceleratorProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketDiscoveryObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillDiscoveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleDiscoveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketDiscoveryObservations", x => x.Id);
                    table.CheckConstraint("CK_MarketObservation_OneConcept", "(\"SkillDiscoveryId\" IS NULL) <> (\"RoleDiscoveryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_MarketDiscoveryObservations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketDiscoveryObservations_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketDiscoveryObservations_MarketRoleDiscoveries_RoleDisco~",
                        column: x => x.RoleDiscoveryId,
                        principalTable: "MarketRoleDiscoveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketDiscoveryObservations_MarketSkillDiscoveries_SkillDis~",
                        column: x => x.SkillDiscoveryId,
                        principalTable: "MarketSkillDiscoveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TalentOutreachRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentOutreachRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalentOutreachRecipients_EmployerContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "EmployerContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentOutreachRecipients_TalentOutreachDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "TalentOutreachDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TalentSubmissionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentSubmissionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalentSubmissionEvents_TalentSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "TalentSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalentSubmissionEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    EvaluationMode = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    PublicPrompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    ExpectedConceptsJson = table.Column<string>(type: "jsonb", nullable: false),
                    HiddenEvaluatorGuidance = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    DeterministicRulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    FollowUpPrompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewQuestions", x => x.Id);
                    table.CheckConstraint("CK_InterviewQuestion_Sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewQuestions_AcceleratorCompetencies_Compe~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewQuestions_AcceleratorInterviewSections_~",
                        column: x => x.SectionId,
                        principalTable: "AcceleratorInterviewSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorReadinessSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerInterviewSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CalculationVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SkillCoverage = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    PracticePerformance = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    ProjectPerformance = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    InterviewPerformance = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorReadinessSnapshots", x => x.Id);
                    table.CheckConstraint("CK_Readiness_Bounds", "\"Total\" BETWEEN 0 AND 100 AND \"SkillCoverage\" BETWEEN 0 AND 100 AND \"PracticePerformance\" BETWEEN 0 AND 100 AND \"ProjectPerformance\" BETWEEN 0 AND 100 AND \"InterviewPerformance\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorReadinessSnapshots_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorReadinessSnapshots_AcceleratorInterviewSessions_~",
                        column: x => x.TriggerInterviewSessionId,
                        principalTable: "AcceleratorInterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MilestoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    EvaluationMode = table.Column<int>(type: "integer", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    CompletionRequirements = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    DeterministicRulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    MaximumAttempts = table.Column<int>(type: "integer", nullable: false),
                    RetryAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CooldownMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectAssignments", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectAssignment_Mode", "\"EvaluationMode\" BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_AcceleratorProjectAssignment_Retry", "\"Sequence\" > 0 AND \"MaximumAttempts\" BETWEEN 1 AND 20 AND \"CooldownMinutes\" BETWEEN 0 AND 10080");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectAssignments_AcceleratorProjectMilestones_~",
                        column: x => x.MilestoneId,
                        principalTable: "AcceleratorProjectMilestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectRubricCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RubricId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectRubricCriteria", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectRubricCriterion_Weight", "\"Sequence\" > 0 AND \"Weight\" > 0 AND \"MaximumScore\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectRubricCriteria_AcceleratorCompetencies_Co~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectRubricCriteria_AcceleratorProjectRubrics_~",
                        column: x => x.RubricId,
                        principalTable: "AcceleratorProjectRubrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorCandidateProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    CurrentMilestoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredRevisionAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorCandidateProjects", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorCandidateProject_Progress", "\"ProgressPercent\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AcceleratorCandidateProjects_AcceleratorCareerGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "AcceleratorCareerGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCandidateProjects_AcceleratorProjectAssignments_~",
                        column: x => x.RequiredRevisionAssignmentId,
                        principalTable: "AcceleratorProjectAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCandidateProjects_AcceleratorProjectMilestones_C~",
                        column: x => x.CurrentMilestoneId,
                        principalTable: "AcceleratorProjectMilestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorCandidateProjects_AcceleratorProjectVersions_Pro~",
                        column: x => x.ProjectVersionId,
                        principalTable: "AcceleratorProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectSubmissions", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectSubmission_Attempt", "\"AttemptNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectSubmissions_AcceleratorCandidateProjects_~",
                        column: x => x.CandidateProjectId,
                        principalTable: "AcceleratorCandidateProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectSubmissions_AcceleratorProjectAssignments~",
                        column: x => x.AssignmentId,
                        principalTable: "AcceleratorProjectAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Evaluator = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectEvaluations", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectEvaluation_Bounds", "\"Score\" >= 0 AND \"MaximumScore\" > 0 AND \"Score\" <= \"MaximumScore\" AND \"Confidence\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectEvaluations_AcceleratorProjectSubmissions~",
                        column: x => x.SubmissionId,
                        principalTable: "AcceleratorProjectSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorProjectEvaluationScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorProjectEvaluationScores", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorProjectEvaluationScore_Nonnegative", "\"Score\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectEvaluationScores_AcceleratorProjectEvalua~",
                        column: x => x.EvaluationId,
                        principalTable: "AcceleratorProjectEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorProjectEvaluationScores_AcceleratorProjectRubric~",
                        column: x => x.CriterionId,
                        principalTable: "AcceleratorProjectRubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorAiDispatchRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InterviewSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    InterviewAnswerId = table.Column<Guid>(type: "uuid", nullable: true),
                    InterviewSectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationSlot = table.Column<int>(type: "integer", nullable: true),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ActualCostInr = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextReconciliationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconciliationAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastReconciliationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReconciliationError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequiresManualReview = table.Column<bool>(type: "boolean", nullable: false),
                    ManualReviewReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReconciliationClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReconciliationClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorAiDispatchRecords", x => x.Id);
                    table.CheckConstraint("CK_AcceleratorAiDispatch_ReconciliationAttempts", "\"ReconciliationAttemptCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorAiDispatchRecords_AcceleratorCandidateProjects_C~",
                        column: x => x.CandidateProjectId,
                        principalTable: "AcceleratorCandidateProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewSections_I~",
                        column: x => x.InterviewSectionId,
                        principalTable: "AcceleratorInterviewSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewSessions_I~",
                        column: x => x.InterviewSessionId,
                        principalTable: "AcceleratorInterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorAiDispatchRecords_AcceleratorProjectSubmissions_~",
                        column: x => x.ProjectSubmissionId,
                        principalTable: "AcceleratorProjectSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorSessionQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentQuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PublicPrompt = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GenerationDispatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationRequestKey = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GenerationProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    GenerationModel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CompetencyIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GenerationMetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    FollowUpEligible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorSessionQuestions", x => x.Id);
                    table.CheckConstraint("CK_SessionQuestion_Sequence", "\"Sequence\" > 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorAiDispatchRecords_Ge~",
                        column: x => x.GenerationDispatchId,
                        principalTable: "AcceleratorAiDispatchRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorCompetencies_Compete~",
                        column: x => x.CompetencyId,
                        principalTable: "AcceleratorCompetencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorInterviewQuestions_S~",
                        column: x => x.SourceQuestionId,
                        principalTable: "AcceleratorInterviewQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorInterviewSections_Se~",
                        column: x => x.SectionId,
                        principalTable: "AcceleratorInterviewSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorInterviewSessions_Se~",
                        column: x => x.SessionId,
                        principalTable: "AcceleratorInterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorSessionQuestions_AcceleratorSessionQuestions_Par~",
                        column: x => x.ParentQuestionId,
                        principalTable: "AcceleratorSessionQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionQuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewAnswers_AcceleratorInterviewSessions_Se~",
                        column: x => x.SessionId,
                        principalTable: "AcceleratorInterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewAnswers_AcceleratorSessionQuestions_Ses~",
                        column: x => x.SessionQuestionId,
                        principalTable: "AcceleratorSessionQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Evaluator = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewEvaluations", x => x.Id);
                    table.CheckConstraint("CK_InterviewEvaluation_Bounds", "\"Score\" BETWEEN 0 AND 100 AND \"Confidence\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewEvaluations_AcceleratorInterviewAnswers~",
                        column: x => x.AnswerId,
                        principalTable: "AcceleratorInterviewAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceleratorInterviewCriterionScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorInterviewCriterionScores", x => x.Id);
                    table.CheckConstraint("CK_InterviewScore_Nonnegative", "\"Score\" >= 0");
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewCriterionScores_AcceleratorInterviewEva~",
                        column: x => x.EvaluationId,
                        principalTable: "AcceleratorInterviewEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorInterviewCriterionScores_AcceleratorInterviewRub~",
                        column: x => x.CriterionId,
                        principalTable: "AcceleratorInterviewRubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId_PurchaseRequestKey",
                table: "Payments",
                columns: new[] { "UserId", "PurchaseRequestKey" },
                unique: true,
                filter: "\"PurchaseRequestKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_AssignmentId",
                table: "AcceleratorSkillEvidence",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_CandidateProjectId",
                table: "AcceleratorSkillEvidence",
                column: "CandidateProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_InterviewEvaluationId_CompetencyId",
                table: "AcceleratorSkillEvidence",
                columns: new[] { "InterviewEvaluationId", "CompetencyId" },
                unique: true,
                filter: "\"InterviewEvaluationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_InterviewSessionId",
                table: "AcceleratorSkillEvidence",
                column: "InterviewSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_ProjectEvaluationId_CompetencyId",
                table: "AcceleratorSkillEvidence",
                columns: new[] { "ProjectEvaluationId", "CompetencyId" },
                unique: true,
                filter: "\"ProjectEvaluationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_ProjectVersionId",
                table: "AcceleratorSkillEvidence",
                column: "ProjectVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_SessionQuestionId",
                table: "AcceleratorSkillEvidence",
                column: "SessionQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSkillEvidence_SubmissionId",
                table: "AcceleratorSkillEvidence",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_CandidateProjectId",
                table: "AcceleratorAiDispatchRecords",
                column: "CandidateProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_InterviewAnswerId",
                table: "AcceleratorAiDispatchRecords",
                column: "InterviewAnswerId",
                unique: true,
                filter: "\"InterviewAnswerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_InterviewSectionId",
                table: "AcceleratorAiDispatchRecords",
                column: "InterviewSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_InterviewSessionId_InterviewSe~",
                table: "AcceleratorAiDispatchRecords",
                columns: new[] { "InterviewSessionId", "InterviewSectionId", "GenerationSlot" },
                unique: true,
                filter: "\"GenerationSlot\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_IsDeleted",
                table: "AcceleratorAiDispatchRecords",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_ProjectSubmissionId",
                table: "AcceleratorAiDispatchRecords",
                column: "ProjectSubmissionId",
                unique: true,
                filter: "\"ProjectSubmissionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_ReservationId",
                table: "AcceleratorAiDispatchRecords",
                column: "ReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_Status_NextReconciliationAtUtc",
                table: "AcceleratorAiDispatchRecords",
                columns: new[] { "Status", "NextReconciliationAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_Status_ReconciliationClaimExpi~",
                table: "AcceleratorAiDispatchRecords",
                columns: new[] { "Status", "ReconciliationClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorAiDispatchRecords_UserId_RequestKey",
                table: "AcceleratorAiDispatchRecords",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_CurrentMilestoneId",
                table: "AcceleratorCandidateProjects",
                column: "CurrentMilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_GoalId",
                table: "AcceleratorCandidateProjects",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_IsDeleted",
                table: "AcceleratorCandidateProjects",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_ProjectVersionId",
                table: "AcceleratorCandidateProjects",
                column: "ProjectVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_RequiredRevisionAssignmentId",
                table: "AcceleratorCandidateProjects",
                column: "RequiredRevisionAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_UserId_CreatedAtUtc",
                table: "AcceleratorCandidateProjects",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_UserId_RequestKey",
                table: "AcceleratorCandidateProjects",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewAnswers_IsDeleted",
                table: "AcceleratorInterviewAnswers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewAnswers_SessionId_RequestKey",
                table: "AcceleratorInterviewAnswers",
                columns: new[] { "SessionId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewAnswers_SessionQuestionId",
                table: "AcceleratorInterviewAnswers",
                column: "SessionQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCompetencies_CompetencyId",
                table: "AcceleratorInterviewCompetencies",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCompetencies_InterviewVersionId_Compete~",
                table: "AcceleratorInterviewCompetencies",
                columns: new[] { "InterviewVersionId", "CompetencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCompetencies_IsDeleted",
                table: "AcceleratorInterviewCompetencies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCriterionScores_CriterionId",
                table: "AcceleratorInterviewCriterionScores",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCriterionScores_EvaluationId_CriterionId",
                table: "AcceleratorInterviewCriterionScores",
                columns: new[] { "EvaluationId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewCriterionScores_IsDeleted",
                table: "AcceleratorInterviewCriterionScores",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewDefinitions_IsDeleted",
                table: "AcceleratorInterviewDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewDefinitions_StableKey",
                table: "AcceleratorInterviewDefinitions",
                column: "StableKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewDefinitions_TargetRoleId_IsActive",
                table: "AcceleratorInterviewDefinitions",
                columns: new[] { "TargetRoleId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewEvaluations_AnswerId",
                table: "AcceleratorInterviewEvaluations",
                column: "AnswerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewEvaluations_IsDeleted",
                table: "AcceleratorInterviewEvaluations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewQuestions_CompetencyId",
                table: "AcceleratorInterviewQuestions",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewQuestions_IsDeleted",
                table: "AcceleratorInterviewQuestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewQuestions_SectionId_Sequence",
                table: "AcceleratorInterviewQuestions",
                columns: new[] { "SectionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewRubricCriteria_CompetencyId",
                table: "AcceleratorInterviewRubricCriteria",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewRubricCriteria_InterviewVersionId_Seque~",
                table: "AcceleratorInterviewRubricCriteria",
                columns: new[] { "InterviewVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewRubricCriteria_IsDeleted",
                table: "AcceleratorInterviewRubricCriteria",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSections_InterviewVersionId_Sequence",
                table: "AcceleratorInterviewSections",
                columns: new[] { "InterviewVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSections_IsDeleted",
                table: "AcceleratorInterviewSections",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_GoalId",
                table: "AcceleratorInterviewSessions",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_InterviewVersionId",
                table: "AcceleratorInterviewSessions",
                column: "InterviewVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_IsDeleted",
                table: "AcceleratorInterviewSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_UserId_RequestKey",
                table: "AcceleratorInterviewSessions",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_UserId_StartedAtUtc",
                table: "AcceleratorInterviewSessions",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewVersions_DefinitionId_Version",
                table: "AcceleratorInterviewVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewVersions_IsDeleted",
                table: "AcceleratorInterviewVersions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPassportShares_GoalId",
                table: "AcceleratorPassportShares",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPassportShares_IsDeleted",
                table: "AcceleratorPassportShares",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPassportShares_PublicToken",
                table: "AcceleratorPassportShares",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPassportShares_UserId_GoalId",
                table: "AcceleratorPassportShares",
                columns: new[] { "UserId", "GoalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeAttempts_IsDeleted",
                table: "AcceleratorPracticeAttempts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeAttempts_QuestionId",
                table: "AcceleratorPracticeAttempts",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeAttempts_SessionId_QuestionId",
                table: "AcceleratorPracticeAttempts",
                columns: new[] { "SessionId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeAttempts_UserId_RequestKey",
                table: "AcceleratorPracticeAttempts",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeEvaluations_AttemptId",
                table: "AcceleratorPracticeEvaluations",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeEvaluations_IsDeleted",
                table: "AcceleratorPracticeEvaluations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeQuestions_IsDeleted",
                table: "AcceleratorPracticeQuestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeQuestions_SessionId_Sequence",
                table: "AcceleratorPracticeQuestions",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_GoalId",
                table: "AcceleratorPracticeSessions",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_IsDeleted",
                table: "AcceleratorPracticeSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_RoadmapNodeId",
                table: "AcceleratorPracticeSessions",
                column: "RoadmapNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_UserId_CompetencyId_CreatedAtUtc",
                table: "AcceleratorPracticeSessions",
                columns: new[] { "UserId", "CompetencyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_UserId_RequestKey",
                table: "AcceleratorPracticeSessions",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectAssignments_IsDeleted",
                table: "AcceleratorProjectAssignments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectAssignments_MilestoneId_Sequence",
                table: "AcceleratorProjectAssignments",
                columns: new[] { "MilestoneId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectCompetencies_CompetencyId",
                table: "AcceleratorProjectCompetencies",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectCompetencies_IsDeleted",
                table: "AcceleratorProjectCompetencies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectCompetencies_ProjectVersionId_CompetencyId",
                table: "AcceleratorProjectCompetencies",
                columns: new[] { "ProjectVersionId", "CompetencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectDefinitions_IsDeleted",
                table: "AcceleratorProjectDefinitions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectDefinitions_StableKey",
                table: "AcceleratorProjectDefinitions",
                column: "StableKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectEvaluations_IsDeleted",
                table: "AcceleratorProjectEvaluations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectEvaluations_SubmissionId",
                table: "AcceleratorProjectEvaluations",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectEvaluationScores_CriterionId",
                table: "AcceleratorProjectEvaluationScores",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectEvaluationScores_EvaluationId_CriterionId",
                table: "AcceleratorProjectEvaluationScores",
                columns: new[] { "EvaluationId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectEvaluationScores_IsDeleted",
                table: "AcceleratorProjectEvaluationScores",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectMilestones_IsDeleted",
                table: "AcceleratorProjectMilestones",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectMilestones_ProjectVersionId_Sequence",
                table: "AcceleratorProjectMilestones",
                columns: new[] { "ProjectVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectRubricCriteria_CompetencyId",
                table: "AcceleratorProjectRubricCriteria",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectRubricCriteria_IsDeleted",
                table: "AcceleratorProjectRubricCriteria",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectRubricCriteria_RubricId_Sequence",
                table: "AcceleratorProjectRubricCriteria",
                columns: new[] { "RubricId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectRubrics_IsDeleted",
                table: "AcceleratorProjectRubrics",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectRubrics_ProjectVersionId_Version",
                table: "AcceleratorProjectRubrics",
                columns: new[] { "ProjectVersionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectSubmissions_AssignmentId",
                table: "AcceleratorProjectSubmissions",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectSubmissions_CandidateProjectId_Assignment~",
                table: "AcceleratorProjectSubmissions",
                columns: new[] { "CandidateProjectId", "AssignmentId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectSubmissions_CandidateProjectId_RequestKey",
                table: "AcceleratorProjectSubmissions",
                columns: new[] { "CandidateProjectId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectSubmissions_IsDeleted",
                table: "AcceleratorProjectSubmissions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectVersions_DefinitionId_Version",
                table: "AcceleratorProjectVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectVersions_IsDeleted",
                table: "AcceleratorProjectVersions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorProjectVersions_TargetRoleId_IsActive_Difficulty",
                table: "AcceleratorProjectVersions",
                columns: new[] { "TargetRoleId", "IsActive", "Difficulty" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_GoalId",
                table: "AcceleratorReadinessSnapshots",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_IsDeleted",
                table: "AcceleratorReadinessSnapshots",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_TriggerInterviewSessionId",
                table: "AcceleratorReadinessSnapshots",
                column: "TriggerInterviewSessionId",
                unique: true,
                filter: "\"TriggerInterviewSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_UserId_GoalId_CalculatedAtUtc",
                table: "AcceleratorReadinessSnapshots",
                columns: new[] { "UserId", "GoalId", "CalculatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_CompetencyId",
                table: "AcceleratorSessionQuestions",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_GenerationDispatchId",
                table: "AcceleratorSessionQuestions",
                column: "GenerationDispatchId",
                unique: true,
                filter: "\"GenerationDispatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_IsDeleted",
                table: "AcceleratorSessionQuestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_ParentQuestionId",
                table: "AcceleratorSessionQuestions",
                column: "ParentQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_SectionId",
                table: "AcceleratorSessionQuestions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_SessionId_GenerationRequestKey",
                table: "AcceleratorSessionQuestions",
                columns: new[] { "SessionId", "GenerationRequestKey" },
                unique: true,
                filter: "\"GenerationRequestKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_SessionId_SectionId_Sequence",
                table: "AcceleratorSessionQuestions",
                columns: new[] { "SessionId", "SectionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_SessionId_SourceQuestionId_Pare~",
                table: "AcceleratorSessionQuestions",
                columns: new[] { "SessionId", "SourceQuestionId", "ParentQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorSessionQuestions_SourceQuestionId",
                table: "AcceleratorSessionQuestions",
                column: "SourceQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorContextSnapshots_ConversationId_CreatedAtUtc",
                table: "AcceleratorTutorContextSnapshots",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorContextSnapshots_IsDeleted",
                table: "AcceleratorTutorContextSnapshots",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_CompetencyId",
                table: "AcceleratorTutorConversations",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_GoalId",
                table: "AcceleratorTutorConversations",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_IsDeleted",
                table: "AcceleratorTutorConversations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_RoadmapNodeId",
                table: "AcceleratorTutorConversations",
                column: "RoadmapNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_UserId_CreatedAtUtc",
                table: "AcceleratorTutorConversations",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_UserId_GoalId_Status",
                table: "AcceleratorTutorConversations",
                columns: new[] { "UserId", "GoalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_UserId_RequestKey",
                table: "AcceleratorTutorConversations",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorMemorySummaries_ConversationId_Version",
                table: "AcceleratorTutorMemorySummaries",
                columns: new[] { "ConversationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorMemorySummaries_IsDeleted",
                table: "AcceleratorTutorMemorySummaries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorMessages_ConversationId_CreatedAtUtc",
                table: "AcceleratorTutorMessages",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorMessages_DispatchId",
                table: "AcceleratorTutorMessages",
                column: "DispatchId",
                unique: true,
                filter: "\"DispatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorMessages_IsDeleted",
                table: "AcceleratorTutorMessages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateMailboxConnections_CandidateId_Provider_ProviderAc~",
                table: "CandidateMailboxConnections",
                columns: new[] { "CandidateId", "Provider", "ProviderAccountIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateMailboxConnections_IsDeleted",
                table: "CandidateMailboxConnections",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerContacts_CompanyId_IsSuppressed_Verification",
                table: "EmployerContacts",
                columns: new[] { "CompanyId", "IsSuppressed", "Verification" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployerContacts_CompanyId_NormalizedEmail",
                table: "EmployerContacts",
                columns: new[] { "CompanyId", "NormalizedEmail" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerContacts_IsDeleted",
                table: "EmployerContacts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_CompanyId",
                table: "MarketDiscoveryObservations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_IsDeleted",
                table: "MarketDiscoveryObservations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_JobId_RoleDiscoveryId",
                table: "MarketDiscoveryObservations",
                columns: new[] { "JobId", "RoleDiscoveryId" },
                unique: true,
                filter: "\"RoleDiscoveryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_JobId_SkillDiscoveryId",
                table: "MarketDiscoveryObservations",
                columns: new[] { "JobId", "SkillDiscoveryId" },
                unique: true,
                filter: "\"SkillDiscoveryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_RoleDiscoveryId_CompanyId",
                table: "MarketDiscoveryObservations",
                columns: new[] { "RoleDiscoveryId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketDiscoveryObservations_SkillDiscoveryId_CompanyId",
                table: "MarketDiscoveryObservations",
                columns: new[] { "SkillDiscoveryId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobProcessingStates_IsDeleted",
                table: "MarketJobProcessingStates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobProcessingStates_JobId",
                table: "MarketJobProcessingStates",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobRoles_IsDeleted",
                table: "MarketJobRoles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobRoles_JobId",
                table: "MarketJobRoles",
                column: "JobId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobRoles_JobId_MarketRoleId",
                table: "MarketJobRoles",
                columns: new[] { "JobId", "MarketRoleId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobRoles_MarketRoleId_JobId",
                table: "MarketJobRoles",
                columns: new[] { "MarketRoleId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobSkills_IsDeleted",
                table: "MarketJobSkills",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobSkills_JobId_MarketSkillId",
                table: "MarketJobSkills",
                columns: new[] { "JobId", "MarketSkillId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketJobSkills_MarketSkillId_JobId",
                table: "MarketJobSkills",
                columns: new[] { "MarketSkillId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketProcessingCheckpoints_IsDeleted",
                table: "MarketProcessingCheckpoints",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketProcessingCheckpoints_LeaseExpiresAtUtc",
                table: "MarketProcessingCheckpoints",
                column: "LeaseExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketProcessingCheckpoints_Name",
                table: "MarketProcessingCheckpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleAliases_IsDeleted",
                table: "MarketRoleAliases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleAliases_MarketRoleId",
                table: "MarketRoleAliases",
                column: "MarketRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleAliases_NormalizedText",
                table: "MarketRoleAliases",
                column: "NormalizedText",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleDiscoveries_IsDeleted",
                table: "MarketRoleDiscoveries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleDiscoveries_NormalizedTitle",
                table: "MarketRoleDiscoveries",
                column: "NormalizedTitle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleDiscoveries_ResolvedMarketRoleId",
                table: "MarketRoleDiscoveries",
                column: "ResolvedMarketRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoleDiscoveries_Status_LastSeenAtUtc",
                table: "MarketRoleDiscoveries",
                columns: new[] { "Status", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_AcceleratorRoleId",
                table: "MarketRoles",
                column: "AcceleratorRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_IsDeleted",
                table: "MarketRoles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_MergedIntoId",
                table: "MarketRoles",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_NormalizedName",
                table: "MarketRoles",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_Slug",
                table: "MarketRoles",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketRoles_Status_Family",
                table: "MarketRoles",
                columns: new[] { "Status", "Family" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillAliases_IsDeleted",
                table: "MarketSkillAliases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillAliases_MarketSkillId",
                table: "MarketSkillAliases",
                column: "MarketSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillAliases_NormalizedText",
                table: "MarketSkillAliases",
                column: "NormalizedText",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillDiscoveries_IsDeleted",
                table: "MarketSkillDiscoveries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillDiscoveries_NormalizedTerm",
                table: "MarketSkillDiscoveries",
                column: "NormalizedTerm",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillDiscoveries_ResolvedMarketSkillId",
                table: "MarketSkillDiscoveries",
                column: "ResolvedMarketSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillDiscoveries_Status_LastSeenAtUtc",
                table: "MarketSkillDiscoveries",
                columns: new[] { "Status", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkillDiscoveries_SuggestedSkillId",
                table: "MarketSkillDiscoveries",
                column: "SuggestedSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_CompetencyId",
                table: "MarketSkills",
                column: "CompetencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_IsDeleted",
                table: "MarketSkills",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_MergedIntoId",
                table: "MarketSkills",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_SkillId",
                table: "MarketSkills",
                column: "SkillId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_Slug",
                table: "MarketSkills",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_MarketSkills_Status_Category",
                table: "MarketSkills",
                columns: new[] { "Status", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_CandidateId_JobId_Status",
                table: "TalentOutreachDrafts",
                columns: new[] { "CandidateId", "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_CandidateId_SendIdempotencyKey",
                table: "TalentOutreachDrafts",
                columns: new[] { "CandidateId", "SendIdempotencyKey" },
                unique: true,
                filter: "\"SendIdempotencyKey\" <> '' AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_CompanyId",
                table: "TalentOutreachDrafts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_IsDeleted",
                table: "TalentOutreachDrafts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_JobId",
                table: "TalentOutreachDrafts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_MailboxConnectionId",
                table: "TalentOutreachDrafts",
                column: "MailboxConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_ShareLinkId",
                table: "TalentOutreachDrafts",
                column: "ShareLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachDrafts_UsageReservationId",
                table: "TalentOutreachDrafts",
                column: "UsageReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachRecipients_ContactId",
                table: "TalentOutreachRecipients",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachRecipients_DraftId_ContactId",
                table: "TalentOutreachRecipients",
                columns: new[] { "DraftId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TalentOutreachRecipients_IsDeleted",
                table: "TalentOutreachRecipients",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_CandidateId_ExpiresAtUtc",
                table: "TalentShareLinks",
                columns: new[] { "CandidateId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_CompanyId",
                table: "TalentShareLinks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_GoalId",
                table: "TalentShareLinks",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_IsDeleted",
                table: "TalentShareLinks",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_JobId",
                table: "TalentShareLinks",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentShareLinks_TokenHash",
                table: "TalentShareLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissionEvents_ActorId",
                table: "TalentSubmissionEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissionEvents_IsDeleted",
                table: "TalentSubmissionEvents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissionEvents_SubmissionId_OccurredAtUtc",
                table: "TalentSubmissionEvents",
                columns: new[] { "SubmissionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_CandidateId_JobId",
                table: "TalentSubmissions",
                columns: new[] { "CandidateId", "JobId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_CompanyId_Status_SubmittedAtUtc",
                table: "TalentSubmissions",
                columns: new[] { "CompanyId", "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_GoalId",
                table: "TalentSubmissions",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_IsDeleted",
                table: "TalentSubmissions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_JobId",
                table: "TalentSubmissions",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentSubmissions_ShareLinkId",
                table: "TalentSubmissions",
                column: "ShareLinkId");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorCandidateProjects_Candi~",
                table: "AcceleratorSkillEvidence",
                column: "CandidateProjectId",
                principalTable: "AcceleratorCandidateProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorInterviewEvaluations_In~",
                table: "AcceleratorSkillEvidence",
                column: "InterviewEvaluationId",
                principalTable: "AcceleratorInterviewEvaluations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorInterviewSessions_Inter~",
                table: "AcceleratorSkillEvidence",
                column: "InterviewSessionId",
                principalTable: "AcceleratorInterviewSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectAssignments_Assi~",
                table: "AcceleratorSkillEvidence",
                column: "AssignmentId",
                principalTable: "AcceleratorProjectAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectEvaluations_Proj~",
                table: "AcceleratorSkillEvidence",
                column: "ProjectEvaluationId",
                principalTable: "AcceleratorProjectEvaluations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectSubmissions_Subm~",
                table: "AcceleratorSkillEvidence",
                column: "SubmissionId",
                principalTable: "AcceleratorProjectSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectVersions_Project~",
                table: "AcceleratorSkillEvidence",
                column: "ProjectVersionId",
                principalTable: "AcceleratorProjectVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorSessionQuestions_Sessio~",
                table: "AcceleratorSkillEvidence",
                column: "SessionQuestionId",
                principalTable: "AcceleratorSessionQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewAnswers_In~",
                table: "AcceleratorAiDispatchRecords",
                column: "InterviewAnswerId",
                principalTable: "AcceleratorInterviewAnswers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorCandidateProjects_Candi~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorInterviewEvaluations_In~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorInterviewSessions_Inter~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectAssignments_Assi~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectEvaluations_Proj~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectSubmissions_Subm~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorProjectVersions_Project~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorSkillEvidence_AcceleratorSessionQuestions_Sessio~",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorAiDispatchRecords_AcceleratorCandidateProjects_C~",
                table: "AcceleratorAiDispatchRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorProjectSubmissions_AcceleratorCandidateProjects_~",
                table: "AcceleratorProjectSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewAnswers_In~",
                table: "AcceleratorAiDispatchRecords");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewCompetencies");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewCriterionScores");

            migrationBuilder.DropTable(
                name: "AcceleratorPassportShares");

            migrationBuilder.DropTable(
                name: "AcceleratorPracticeAttempts");

            migrationBuilder.DropTable(
                name: "AcceleratorPracticeEvaluations");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectCompetencies");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectEvaluationScores");

            migrationBuilder.DropTable(
                name: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropTable(
                name: "AcceleratorTutorContextSnapshots");

            migrationBuilder.DropTable(
                name: "AcceleratorTutorMemorySummaries");

            migrationBuilder.DropTable(
                name: "AcceleratorTutorMessages");

            migrationBuilder.DropTable(
                name: "MarketDiscoveryObservations");

            migrationBuilder.DropTable(
                name: "MarketJobProcessingStates");

            migrationBuilder.DropTable(
                name: "MarketJobRoles");

            migrationBuilder.DropTable(
                name: "MarketJobSkills");

            migrationBuilder.DropTable(
                name: "MarketProcessingCheckpoints");

            migrationBuilder.DropTable(
                name: "MarketRoleAliases");

            migrationBuilder.DropTable(
                name: "MarketSkillAliases");

            migrationBuilder.DropTable(
                name: "TalentOutreachRecipients");

            migrationBuilder.DropTable(
                name: "TalentSubmissionEvents");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewEvaluations");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewRubricCriteria");

            migrationBuilder.DropTable(
                name: "AcceleratorPracticeQuestions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectEvaluations");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectRubricCriteria");

            migrationBuilder.DropTable(
                name: "AcceleratorTutorConversations");

            migrationBuilder.DropTable(
                name: "MarketRoleDiscoveries");

            migrationBuilder.DropTable(
                name: "MarketSkillDiscoveries");

            migrationBuilder.DropTable(
                name: "EmployerContacts");

            migrationBuilder.DropTable(
                name: "TalentOutreachDrafts");

            migrationBuilder.DropTable(
                name: "TalentSubmissions");

            migrationBuilder.DropTable(
                name: "AcceleratorPracticeSessions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectRubrics");

            migrationBuilder.DropTable(
                name: "MarketRoles");

            migrationBuilder.DropTable(
                name: "MarketSkills");

            migrationBuilder.DropTable(
                name: "CandidateMailboxConnections");

            migrationBuilder.DropTable(
                name: "TalentShareLinks");

            migrationBuilder.DropTable(
                name: "AcceleratorCandidateProjects");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewAnswers");

            migrationBuilder.DropTable(
                name: "AcceleratorSessionQuestions");

            migrationBuilder.DropTable(
                name: "AcceleratorAiDispatchRecords");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewQuestions");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewSessions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectSubmissions");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewSections");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectAssignments");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewVersions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectMilestones");

            migrationBuilder.DropTable(
                name: "AcceleratorInterviewDefinitions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectVersions");

            migrationBuilder.DropTable(
                name: "AcceleratorProjectDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId_PurchaseRequestKey",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_AssignmentId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_CandidateProjectId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_InterviewEvaluationId_CompetencyId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_InterviewSessionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_ProjectEvaluationId_CompetencyId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_ProjectVersionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_SessionQuestionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorSkillEvidence_SubmissionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "AcceleratorRefundReviewAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcceleratorRefundReviewReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcceleratorRefundReviewRequired",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PlanVersion",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderCheckoutExpiresAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderCheckoutUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PurchaseRequestKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AssignmentId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "CandidateProjectId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "InterviewEvaluationId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "InterviewSessionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "ProjectEvaluationId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "ProjectVersionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "SessionQuestionId",
                table: "AcceleratorSkillEvidence");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "AcceleratorSkillEvidence");
        }
    }
}
