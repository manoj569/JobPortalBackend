using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidanceSessionsAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareerGuidanceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeetingProvider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderMeetingId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProtectedParticipantUrl = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectedHostUrl = table.Column<byte[]>(type: "bytea", nullable: true),
                    ScheduledStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CandidateJoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsultantJoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CandidateNoShowMarkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsultantNoShowMarkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsultantNoShowReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MeetingProvisioningAttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MeetingCreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EarningReleaseDelayHours = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceSessions", x => x.Id);
                    table.CheckConstraint("CK_CGSession_Interval", "\"ScheduledStartUtc\" < \"ScheduledEndUtc\"");
                    table.CheckConstraint("CK_CGSession_ReleaseDelay", "\"EarningReleaseDelayHours\" BETWEEN 24 AND 2160");
                    table.CheckConstraint("CK_CGSession_Status", "\"Status\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceSessions_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceSessions_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceSessions_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceSessionReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceSessionReminders", x => x.Id);
                    table.CheckConstraint("CK_CGReminder_Offset", "\"OffsetMinutes\" BETWEEN 1 AND 10080");
                    table.CheckConstraint("CK_CGReminder_Status", "\"Status\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceSessionReminders_CareerGuidanceSessions_Sessi~",
                        column: x => x.SessionId,
                        principalTable: "CareerGuidanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceSessionReminders_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessionReminders_IsDeleted",
                table: "CareerGuidanceSessionReminders",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessionReminders_RecipientUserId",
                table: "CareerGuidanceSessionReminders",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessionReminders_SessionId_RecipientUserId_Of~",
                table: "CareerGuidanceSessionReminders",
                columns: new[] { "SessionId", "RecipientUserId", "OffsetMinutes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessionReminders_Status_ScheduledForUtc",
                table: "CareerGuidanceSessionReminders",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessions_BookingId",
                table: "CareerGuidanceSessions",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessions_CandidateUserId_Status_ScheduledStar~",
                table: "CareerGuidanceSessions",
                columns: new[] { "CandidateUserId", "Status", "ScheduledStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessions_ConsultantId_Status_ScheduledStartUtc",
                table: "CareerGuidanceSessions",
                columns: new[] { "ConsultantId", "Status", "ScheduledStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessions_IsDeleted",
                table: "CareerGuidanceSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceSessions_MeetingProvider_ProviderMeetingId",
                table: "CareerGuidanceSessions",
                columns: new[] { "MeetingProvider", "ProviderMeetingId" },
                unique: true,
                filter: "\"ProviderMeetingId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerGuidanceSessionReminders");

            migrationBuilder.DropTable(
                name: "CareerGuidanceSessions");
        }
    }
}
