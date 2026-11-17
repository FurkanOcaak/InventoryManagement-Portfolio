using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccess.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoSeedLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoSeedRuns",
                columns: table => new
                {
                    DatasetId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PlanHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoSeedRuns", x => x.DatasetId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoSeedRuns");
        }
    }
}
