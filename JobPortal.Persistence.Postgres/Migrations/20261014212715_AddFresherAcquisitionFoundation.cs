using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFresherAcquisitionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcquisitionExternalId",
                table: "Jobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionSourceId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalJobUrl",
                table: "Jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveredUrl",
                table: "Jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAtUtc",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcquisitionSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoPublishEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionSources", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AcquisitionSources",
                columns: new[] { "Id", "ProviderKey", "Name", "IsEnabled", "AutoPublishEnabled", "CreatedAtUtc", "IsDeleted" },
                values: new object[] { new Guid("b20d2adf-90e2-449c-9d2b-19381ce4a3f1"), "serpapi-google-jobs", "SerpApi Google Jobs", true, false, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), false });

            migrationBuilder.CreateTable(
                name: "AcquisitionImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleCount = table.Column<int>(type: "integer", nullable: false),
                    RejectedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    ExistingCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    FailureSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionImportRuns_AcquisitionSources_AcquisitionSourceId",
                        column: x => x.AcquisitionSourceId,
                        principalTable: "AcquisitionSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcquisitionImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquisitionSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemIndex = table.Column<int>(type: "integer", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentityKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DiscoveredUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CanonicalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ApplyUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CompanyNameNormalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleNormalized = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    LocationNormalized = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    EligibilityDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcquisitionImportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcquisitionImportItems_AcquisitionImportRuns_AcquisitionImp~",
                        column: x => x.AcquisitionImportRunId,
                        principalTable: "AcquisitionImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AcquisitionImportItems_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_AcquisitionSourceId_AcquisitionExternalId",
                table: "Jobs",
                columns: new[] { "AcquisitionSourceId", "AcquisitionExternalId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"AcquisitionSourceId\" IS NOT NULL AND \"AcquisitionExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportItems_AcquisitionImportRunId_IdentityKey",
                table: "AcquisitionImportItems",
                columns: new[] { "AcquisitionImportRunId", "IdentityKey" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportItems_AcquisitionSourceId_ExternalId_LastS~",
                table: "AcquisitionImportItems",
                columns: new[] { "AcquisitionSourceId", "ExternalId", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportItems_IsDeleted",
                table: "AcquisitionImportItems",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportItems_JobId",
                table: "AcquisitionImportItems",
                column: "JobId",
                filter: "\"JobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportRuns_AcquisitionSourceId_BatchId",
                table: "AcquisitionImportRuns",
                columns: new[] { "AcquisitionSourceId", "BatchId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportRuns_AcquisitionSourceId_StartedAtUtc",
                table: "AcquisitionImportRuns",
                columns: new[] { "AcquisitionSourceId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportRuns_IsDeleted",
                table: "AcquisitionImportRuns",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionSources_IsDeleted",
                table: "AcquisitionSources",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionSources_ProviderKey",
                table: "AcquisitionSources",
                column: "ProviderKey",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_AcquisitionSources_AcquisitionSourceId",
                table: "Jobs",
                column: "AcquisitionSourceId",
                principalTable: "AcquisitionSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_AcquisitionSources_AcquisitionSourceId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "AcquisitionImportItems");

            migrationBuilder.DropTable(
                name: "AcquisitionImportRuns");

            migrationBuilder.DropTable(
                name: "AcquisitionSources");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_AcquisitionSourceId_AcquisitionExternalId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AcquisitionExternalId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AcquisitionSourceId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CanonicalJobUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DiscoveredUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAtUtc",
                table: "Jobs");
        }
    }
}
