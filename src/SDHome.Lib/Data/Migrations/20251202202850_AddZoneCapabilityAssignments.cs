using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDHome.Lib.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneCapabilityAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "zone_capability_assignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    zone_id = table.Column<int>(type: "int", nullable: false),
                    capability = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    device_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    property = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zone_capability_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_zone_capability_assignments_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "device_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_zone_capability_assignments_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_zone_capability_assignments_capability",
                table: "zone_capability_assignments",
                column: "capability");

            migrationBuilder.CreateIndex(
                name: "idx_zone_capability_assignments_device_id",
                table: "zone_capability_assignments",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "idx_zone_capability_assignments_unique",
                table: "zone_capability_assignments",
                columns: new[] { "zone_id", "capability", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_zone_capability_assignments_zone_id",
                table: "zone_capability_assignments",
                column: "zone_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zone_capability_assignments");
        }
    }
}
