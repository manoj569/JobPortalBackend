using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorPhase3JourneyContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema already exists in AddEmployerHiringPipeline. Do not duplicate columns or constraints.
            migrationBuilder.Sql(Phase3JourneyBackfillSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep canonical contexts and historical attribution; never delete evidence on rollback.
            migrationBuilder.Sql(Phase3JourneyBackfillSql.AssertLegacyOnly);
        }
    }
}
