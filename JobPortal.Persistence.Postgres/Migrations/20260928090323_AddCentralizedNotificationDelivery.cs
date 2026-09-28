using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralizedNotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessKey",
                table: "Notifications",
                type: "character varying(220)",
                maxLength: 220,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderOffsetMinutes",
                table: "CandidateInterviewSchedules",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<Guid>(
    name: "ReminderRevision",
    table: "CandidateInterviewSchedules",
    type: "uuid",
    nullable: false,
    defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "CandidateInterviewSchedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRevision = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.CheckConstraint("CK_NotificationDeliveries_Attempts", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_NotificationDeliveries_Channel", "\"Channel\" IN (1, 2)");
                    table.CheckConstraint("CK_NotificationDeliveries_Status", "\"Status\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_BusinessKey",
                table: "Notifications",
                columns: new[] { "UserId", "BusinessKey" },
                unique: true,
                filter: "\"BusinessKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CandidateInterviewSchedules_ReminderOffset",
                table: "CandidateInterviewSchedules",
                sql: "\"ReminderOffsetMinutes\" IN (15, 30, 60, 1440)");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_BusinessKey_UserId_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "BusinessKey", "UserId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_IsDeleted",
                table: "NotificationDeliveries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationId_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Source_SourceId_SourceRevision",
                table: "NotificationDeliveries",
                columns: new[] { "Source", "SourceId", "SourceRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_LeaseExpiresAtUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAtUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_UserId",
                table: "NotificationDeliveries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_BusinessKey",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CandidateInterviewSchedules_ReminderOffset",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "BusinessKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReminderOffsetMinutes",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "ReminderRevision",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "CandidateInterviewSchedules");
        }
    }
}
