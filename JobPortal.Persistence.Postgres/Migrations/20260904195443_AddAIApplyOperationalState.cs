using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAIApplyOperationalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIApplySiteOperationalStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Site = table.Column<int>(type: "integer", nullable: false),
                    CircuitState = table.Column<int>(type: "integer", nullable: false),
                    ObservationWindowStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CooldownUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HalfOpenProbeOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HalfOpenProbeLeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManualOverrideOpen = table.Column<bool>(type: "boolean", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplySiteOperationalStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyWorkerInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerInstanceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulPollAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StoppedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingCount = table.Column<int>(type: "integer", nullable: false),
                    MaximumConcurrency = table.Column<int>(type: "integer", nullable: false),
                    HostVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyWorkerInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySiteOperationalStates_CircuitState_CooldownUntilUtc",
                table: "AIApplySiteOperationalStates",
                columns: new[] { "CircuitState", "CooldownUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySiteOperationalStates_IsDeleted",
                table: "AIApplySiteOperationalStates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySiteOperationalStates_Site",
                table: "AIApplySiteOperationalStates",
                column: "Site",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyWorkerInstances_IsDeleted",
                table: "AIApplyWorkerInstances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyWorkerInstances_Status_LastHeartbeatAtUtc",
                table: "AIApplyWorkerInstances",
                columns: new[] { "Status", "LastHeartbeatAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyWorkerInstances_WorkerInstanceId",
                table: "AIApplyWorkerInstances",
                column: "WorkerInstanceId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.InsertData(
                table: "AIApplySiteOperationalStates",
                columns: new[] { "Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted" },
                values: new object[,]
                {
                    { new Guid("39000000-0000-0000-0000-000000000000"), 0, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000001"), 1, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000002"), 2, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000003"), 3, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000004"), 4, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000005"), 5, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000006"), 6, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000007"), 7, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000008"), 8, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000009"), 9, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false },
                    { new Guid("39000000-0000-0000-0000-000000000010"), 10, 1, DateTime.UnixEpoch, 0, 0, 0L, DateTime.UnixEpoch, false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIApplySiteOperationalStates");

            migrationBuilder.DropTable(
                name: "AIApplyWorkerInstances");
        }
    }
}
