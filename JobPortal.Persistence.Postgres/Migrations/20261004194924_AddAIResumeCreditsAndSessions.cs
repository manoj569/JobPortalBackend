using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAIResumeCreditsAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIResumeCreditWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    LifetimePurchased = table.Column<int>(type: "integer", nullable: false),
                    LifetimeConsumed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResumeCreditWallets", x => x.Id);
                    table.CheckConstraint("CK_AIResumeWallet_Nonnegative", "\"Balance\" >= 0 AND \"Reserved\" >= 0 AND \"LifetimePurchased\" >= 0 AND \"LifetimeConsumed\" >= 0 AND \"LifetimePurchased\" = \"Balance\" + \"Reserved\" + \"LifetimeConsumed\"");
                    table.ForeignKey(
                        name: "FK_AIResumeCreditWallets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIResumeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    JobDescription = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    SourceJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnalysisJson = table.Column<string>(type: "jsonb", nullable: true),
                    AnalysisModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AnalysisInputTokens = table.Column<int>(type: "integer", nullable: false),
                    AnalysisOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AnalysisOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    AnalysisLeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResumeSessions", x => x.Id);
                    table.CheckConstraint("CK_AIResumeSessions_Status", "\"Status\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_AIResumeSessions_CandidateResumeProfiles_SourceResumeId",
                        column: x => x.SourceResumeId,
                        principalTable: "CandidateResumeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumeSessions_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumeSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIResumeGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Owner = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResumeGenerations", x => x.Id);
                    table.CheckConstraint("CK_AIResumeGeneration_Status", "\"Status\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_AIResumeGenerations_AIResumeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AIResumeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumeGenerations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIResumePurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MerchantOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RedirectUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckoutOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckoutLeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResumePurchases", x => x.Id);
                    table.CheckConstraint("CK_AIResumePurchase_Snapshot", "\"Credits\" > 0 AND \"Amount\" > 0 AND \"CurrencyCode\" = 'INR'");
                    table.ForeignKey(
                        name: "FK_AIResumePurchases_AIResumeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AIResumeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumePurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoredResumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EditRevision = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GenerationModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TailoredResumes", x => x.Id);
                    table.CheckConstraint("CK_TailoredResume_Version", "\"Version\" > 0 AND \"EditRevision\" >= 0");
                    table.ForeignKey(
                        name: "FK_TailoredResumes_AIResumeGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "AIResumeGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TailoredResumes_AIResumeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AIResumeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TailoredResumes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIResumeCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AvailableDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    ReservedAfter = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResumeCreditTransactions", x => x.Id);
                    table.CheckConstraint("CK_AIResumeLedger_Nonnegative", "\"BalanceAfter\" >= 0 AND \"ReservedAfter\" >= 0");
                    table.ForeignKey(
                        name: "FK_AIResumeCreditTransactions_AIResumeGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "AIResumeGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumeCreditTransactions_AIResumePurchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "AIResumePurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIResumeCreditTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoredResumeEdits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TailoredResumeEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TailoredResumeEdits_TailoredResumes_ResumeId",
                        column: x => x.ResumeId,
                        principalTable: "TailoredResumes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditTransactions_GenerationId_Kind",
                table: "AIResumeCreditTransactions",
                columns: new[] { "GenerationId", "Kind" },
                filter: "\"GenerationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditTransactions_IdempotencyKey",
                table: "AIResumeCreditTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditTransactions_IsDeleted",
                table: "AIResumeCreditTransactions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditTransactions_PurchaseId_Kind",
                table: "AIResumeCreditTransactions",
                columns: new[] { "PurchaseId", "Kind" },
                unique: true,
                filter: "\"PurchaseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditTransactions_UserId_CreatedAtUtc",
                table: "AIResumeCreditTransactions",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditWallets_IsDeleted",
                table: "AIResumeCreditWallets",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeCreditWallets_UserId",
                table: "AIResumeCreditWallets",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeGenerations_IsDeleted",
                table: "AIResumeGenerations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeGenerations_SessionId",
                table: "AIResumeGenerations",
                column: "SessionId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeGenerations_Status_LeaseUntilUtc",
                table: "AIResumeGenerations",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeGenerations_UserId_RequestKey",
                table: "AIResumeGenerations",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIResumePurchases_IsDeleted",
                table: "AIResumePurchases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumePurchases_MerchantOrderId",
                table: "AIResumePurchases",
                column: "MerchantOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIResumePurchases_SessionId",
                table: "AIResumePurchases",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumePurchases_UserId_RequestKey",
                table: "AIResumePurchases",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeSessions_IsDeleted",
                table: "AIResumeSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeSessions_JobId",
                table: "AIResumeSessions",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeSessions_SourceResumeId",
                table: "AIResumeSessions",
                column: "SourceResumeId");

            migrationBuilder.CreateIndex(
                name: "IX_AIResumeSessions_UserId_CreatedAtUtc",
                table: "AIResumeSessions",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumeEdits_IsDeleted",
                table: "TailoredResumeEdits",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumeEdits_ResumeId_Revision",
                table: "TailoredResumeEdits",
                columns: new[] { "ResumeId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumes_GenerationId",
                table: "TailoredResumes",
                column: "GenerationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumes_IsDeleted",
                table: "TailoredResumes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumes_SessionId_Version",
                table: "TailoredResumes",
                columns: new[] { "SessionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TailoredResumes_UserId_CreatedAtUtc",
                table: "TailoredResumes",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIResumeCreditTransactions");

            migrationBuilder.DropTable(
                name: "AIResumeCreditWallets");

            migrationBuilder.DropTable(
                name: "TailoredResumeEdits");

            migrationBuilder.DropTable(
                name: "AIResumePurchases");

            migrationBuilder.DropTable(
                name: "TailoredResumes");

            migrationBuilder.DropTable(
                name: "AIResumeGenerations");

            migrationBuilder.DropTable(
                name: "AIResumeSessions");
        }
    }
}
