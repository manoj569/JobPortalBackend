using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddJobDescriptionClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "Jobs",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationJson",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualClassificationOverrides",
                table: "Jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalaryPeriod",
                table: "Jobs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassificationJson",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ManualClassificationOverrides",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SalaryPeriod",
                table: "Jobs");

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode",
                table: "Jobs",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldNullable: true);
        }
    }
}
