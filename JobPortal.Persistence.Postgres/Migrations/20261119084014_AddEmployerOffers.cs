using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployerOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferAtUtc",
                table: "EmployerHiringPipelines");

            migrationBuilder.DropColumn(
                name: "OfferNotes",
                table: "EmployerHiringPipelines");

            migrationBuilder.DropColumn(
                name: "OfferStatus",
                table: "EmployerHiringPipelines");

            migrationBuilder.CreateTable(
                name: "EmployerOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AnnualSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    JoiningDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkLocation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OfferLetterUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerOffers", x => x.Id);
                    table.CheckConstraint("CK_EmployerOffers_AnnualSalary", "\"AnnualSalary\" IS NULL OR \"AnnualSalary\" >= 0");
                    table.ForeignKey(
                        name: "FK_EmployerOffers_EmployerHiringPipelines_PipelineId",
                        column: x => x.PipelineId,
                        principalTable: "EmployerHiringPipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployerOffers_JobApplications_JobApplicationId",
                        column: x => x.JobApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployerOffers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployerOffers_CreatedByUserId",
                table: "EmployerOffers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerOffers_IsDeleted",
                table: "EmployerOffers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerOffers_JobApplicationId",
                table: "EmployerOffers",
                column: "JobApplicationId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerOffers_PipelineId",
                table: "EmployerOffers",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerOffers_Status_ExpiresAtUtc",
                table: "EmployerOffers",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployerOffers");

            migrationBuilder.AddColumn<DateTime>(
                name: "OfferAtUtc",
                table: "EmployerHiringPipelines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferNotes",
                table: "EmployerHiringPipelines",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OfferStatus",
                table: "EmployerHiringPipelines",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
