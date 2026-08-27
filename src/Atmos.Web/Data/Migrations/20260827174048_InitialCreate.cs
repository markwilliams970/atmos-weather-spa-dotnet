using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atmos.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecentSearch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "char(32)", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    ElevationMeters = table.Column<double>(type: "float", nullable: true),
                    Units = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "Imperial"),
                    LocationType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "Zip"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    LastAccessedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentSearch", x => x.Id);
                    table.CheckConstraint("CK_RecentSearch_Latitude", "[Latitude] BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_RecentSearch_Longitude", "[Longitude] BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecentSearch_SessionId_Label",
                table: "RecentSearch",
                columns: new[] { "SessionId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentSearch_SessionId_LastAccessedUtc",
                table: "RecentSearch",
                columns: new[] { "SessionId", "LastAccessedUtc" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Label", "Latitude", "Longitude", "ElevationMeters", "Units", "LocationType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecentSearch");
        }
    }
}
