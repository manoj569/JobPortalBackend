using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidanceSchedulingAndBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.AddColumn<bool>(
                name: "IsAcceptingBookings",
                table: "CareerConsultants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "CareerConsultants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareerConsultantAvailability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantAvailability", x => x.Id);
                    table.CheckConstraint("CK_CareerAvailability_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_CareerAvailability_Time", "\"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_CareerConsultantAvailability_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerConsultantAvailabilityExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantAvailabilityExceptions", x => x.Id);
                    table.CheckConstraint("CK_CareerAvailabilityException_Time", "(\"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR (\"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"StartTime\" < \"EndTime\")");
                    table.ForeignKey(
                        name: "FK_CareerConsultantAvailabilityExceptions_CareerConsultants_Co~",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsultantTimeZoneSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ServiceTitleSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ServiceTypeSnapshot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DurationMinutesSnapshot = table.Column<int>(type: "integer", nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencySnapshot = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetCompany = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetRole = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    YearsOfExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    CurrentRoleOrStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SessionGoal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Questions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceBookings", x => x.Id);
                    table.CheckConstraint("CK_CareerBooking_Duration", "\"DurationMinutesSnapshot\" BETWEEN 15 AND 180");
                    table.CheckConstraint("CK_CareerBooking_Price", "\"PriceSnapshot\" > 0");
                    table.CheckConstraint("CK_CareerBooking_Status", "\"Status\" BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_CareerBooking_Time", "\"StartUtc\" < \"EndUtc\"");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceBookings_CareerConsultantServices_ConsultantS~",
                        column: x => x.ConsultantServiceId,
                        principalTable: "CareerConsultantServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceBookings_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceBookings_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceBookings_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantAvailability_ConsultantId_DayOfWeek_IsActive",
                table: "CareerConsultantAvailability",
                columns: new[] { "ConsultantId", "DayOfWeek", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantAvailability_IsDeleted",
                table: "CareerConsultantAvailability",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantAvailabilityExceptions_ConsultantId_LocalDa~",
                table: "CareerConsultantAvailabilityExceptions",
                columns: new[] { "ConsultantId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantAvailabilityExceptions_IsDeleted",
                table: "CareerConsultantAvailabilityExceptions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_CancelledByUserId",
                table: "CareerGuidanceBookings",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_CandidateUserId_Status_StartUtc",
                table: "CareerGuidanceBookings",
                columns: new[] { "CandidateUserId", "Status", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_ConsultantId_Status_StartUtc_EndUtc",
                table: "CareerGuidanceBookings",
                columns: new[] { "ConsultantId", "Status", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_ConsultantServiceId",
                table: "CareerGuidanceBookings",
                column: "ConsultantServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_IsDeleted",
                table: "CareerGuidanceBookings",
                column: "IsDeleted");

            // EF has no native exclusion-constraint mapping. Half-open ranges allow
            // adjacent sessions; pending and confirmed bookings reserve the interval.
            migrationBuilder.Sql("""
                ALTER TABLE "CareerGuidanceBookings"
                ADD CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap"
                EXCLUDE USING gist (
                    "ConsultantId" WITH =,
                    tstzrange("StartUtc", "EndUtc", '[)') WITH &&
                ) WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerConsultantAvailability");

            migrationBuilder.DropTable(
                name: "CareerConsultantAvailabilityExceptions");

            migrationBuilder.DropTable(
                name: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "IsAcceptingBookings",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "CareerConsultants");

            // Keep the database-wide extension: other objects may also depend on it.
        }
    }
}
