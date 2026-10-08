using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableJobSourceRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobSourceRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InterruptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Processed = table.Column<int>(type: "integer", nullable: false),
                    TotalReceived = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Closed = table.Column<int>(type: "integer", nullable: false),
                    Matched = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Published = table.Column<int>(type: "integer", nullable: false),
                    NeedsReview = table.Column<int>(type: "integer", nullable: false),
                    QualityRejected = table.Column<int>(type: "integer", nullable: false),
                    PublishFailed = table.Column<int>(type: "integer", nullable: false),
                    AutoPublishDisabled = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSourceRuns", x => x.Id);
                    table.CheckConstraint("CK_JobSourceRuns_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_JobSourceRuns_Lease", "(\"Status\" = 1 AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LeaseOwner\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_JobSourceRuns_Status", "\"Status\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_JobSourceRuns_JobSources_JobSourceId",
                        column: x => x.JobSourceId,
                        principalTable: "JobSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobSourceRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSourceRuns_JobSourceId_QueuedAtUtc",
                table: "JobSourceRuns",
                columns: new[] { "JobSourceId", "QueuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobSourceRuns_RequestedByUserId",
                table: "JobSourceRuns",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobSourceRuns_Status_LeaseExpiresAtUtc",
                table: "JobSourceRuns",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobSourceRuns_Status_NextAttemptAtUtc",
                table: "JobSourceRuns",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_JobSourceRuns_ActiveSource",
                table: "JobSourceRuns",
                column: "JobSourceId",
                unique: true,
                filter: "\"Status\" IN (0, 1) OR (\"Status\" = 4 AND \"AttemptCount\" < 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSourceRuns");
        }
    }
}
