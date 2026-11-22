using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateHiringTeamOutreach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringMailboxAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrowserHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedVerifier = table.Column<string>(type: "text", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringMailboxAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringMailboxAuthorizations_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringMailboxSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringMailboxSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringMailboxSecrets_CandidateMailboxConnections_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "CandidateMailboxConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringOutreachSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CompanyDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    JobDescription = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UsageState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AllowanceSource = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringOutreachSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSessions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSessions_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSessions_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringOutreachContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Verification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    LinkedInUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringOutreachContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringOutreachContacts_HiringOutreachSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "HiringOutreachSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringOutreachDiscoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringOutreachDiscoveryRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringOutreachDiscoveryRequests_HiringOutreachSessions_Sess~",
                        column: x => x.SessionId,
                        principalTable: "HiringOutreachSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachDiscoveryRequests_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachDiscoveryRequests_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringOutreachDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringOutreachDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringOutreachDrafts_HiringOutreachSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "HiringOutreachSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachDrafts_ResumeDocumentBlobs_ResumeId",
                        column: x => x.ResumeId,
                        principalTable: "ResumeDocumentBlobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringOutreachSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProviderThreadId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringOutreachSends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_HiringOutreachContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "HiringOutreachContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_HiringOutreachDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "HiringOutreachDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_HiringOutreachSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "HiringOutreachSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_ResumeDocumentBlobs_ResumeId",
                        column: x => x.ResumeId,
                        principalTable: "ResumeDocumentBlobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringOutreachSends_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringMailboxAuthorizations_CandidateId",
                table: "HiringMailboxAuthorizations",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringMailboxAuthorizations_IsDeleted",
                table: "HiringMailboxAuthorizations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringMailboxAuthorizations_StateHash",
                table: "HiringMailboxAuthorizations",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringMailboxSecrets_IsDeleted",
                table: "HiringMailboxSecrets",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringMailboxSecrets_MailboxId",
                table: "HiringMailboxSecrets",
                column: "MailboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachContacts_IsDeleted",
                table: "HiringOutreachContacts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachContacts_SessionId_Email",
                table: "HiringOutreachContacts",
                columns: new[] { "SessionId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDiscoveryRequests_CandidateId_RequestKey",
                table: "HiringOutreachDiscoveryRequests",
                columns: new[] { "CandidateId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDiscoveryRequests_IsDeleted",
                table: "HiringOutreachDiscoveryRequests",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDiscoveryRequests_JobId",
                table: "HiringOutreachDiscoveryRequests",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDiscoveryRequests_SessionId",
                table: "HiringOutreachDiscoveryRequests",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDrafts_IsDeleted",
                table: "HiringOutreachDrafts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDrafts_ResumeId",
                table: "HiringOutreachDrafts",
                column: "ResumeId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachDrafts_SessionId",
                table: "HiringOutreachDrafts",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_CandidateId_CreatedAtUtc",
                table: "HiringOutreachSends",
                columns: new[] { "CandidateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_CandidateId_JobId_EmailSnapshot",
                table: "HiringOutreachSends",
                columns: new[] { "CandidateId", "JobId", "EmailSnapshot" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_CandidateId_SessionId_ContactId_Request~",
                table: "HiringOutreachSends",
                columns: new[] { "CandidateId", "SessionId", "ContactId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_ContactId",
                table: "HiringOutreachSends",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_DraftId",
                table: "HiringOutreachSends",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_IsDeleted",
                table: "HiringOutreachSends",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_JobId",
                table: "HiringOutreachSends",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_ResumeId",
                table: "HiringOutreachSends",
                column: "ResumeId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSends_SessionId",
                table: "HiringOutreachSends",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_CandidateId_DispatchedAtUtc",
                table: "HiringOutreachSessions",
                columns: new[] { "CandidateId", "DispatchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_CandidateId_JobId_ExpiresAtUtc",
                table: "HiringOutreachSessions",
                columns: new[] { "CandidateId", "JobId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_CandidateId_RequestKey",
                table: "HiringOutreachSessions",
                columns: new[] { "CandidateId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_CompanyId",
                table: "HiringOutreachSessions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_IsDeleted",
                table: "HiringOutreachSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringOutreachSessions_JobId",
                table: "HiringOutreachSessions",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringMailboxAuthorizations");

            migrationBuilder.DropTable(
                name: "HiringMailboxSecrets");

            migrationBuilder.DropTable(
                name: "HiringOutreachDiscoveryRequests");

            migrationBuilder.DropTable(
                name: "HiringOutreachSends");

            migrationBuilder.DropTable(
                name: "HiringOutreachContacts");

            migrationBuilder.DropTable(
                name: "HiringOutreachDrafts");

            migrationBuilder.DropTable(
                name: "HiringOutreachSessions");
        }
    }
}
