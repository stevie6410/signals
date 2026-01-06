using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SDHome.Lib.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_trigger_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    trigger_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    device_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    metric = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "nvarchar(50)", maxLength: 50, nullable: false),
                    threshold = table.Column<double>(type: "float", nullable: false),
                    threshold2 = table.Column<double>(type: "float", nullable: true),
                    cooldown_seconds = table.Column<int>(type: "int", nullable: true),
                    last_fired_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_trigger_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_trigger_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    custom_trigger_rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fired_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    device_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    metric = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<double>(type: "float", nullable: false),
                    condition = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    generated_trigger_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_trigger_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_custom_trigger_logs_custom_trigger_rules_custom_trigger_rule_id",
                        column: x => x.custom_trigger_rule_id,
                        principalTable: "custom_trigger_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_custom_trigger_logs_fired_utc",
                table: "custom_trigger_logs",
                column: "fired_utc");

            migrationBuilder.CreateIndex(
                name: "idx_custom_trigger_logs_rule_id",
                table: "custom_trigger_logs",
                column: "custom_trigger_rule_id");

            migrationBuilder.CreateIndex(
                name: "idx_custom_triggers_device_id",
                table: "custom_trigger_rules",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "idx_custom_triggers_device_metric",
                table: "custom_trigger_rules",
                columns: new[] { "device_id", "metric" });

            migrationBuilder.CreateIndex(
                name: "idx_custom_triggers_enabled",
                table: "custom_trigger_rules",
                column: "enabled");

            migrationBuilder.CreateIndex(
                name: "idx_custom_triggers_metric",
                table: "custom_trigger_rules",
                column: "metric");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_trigger_logs");

            migrationBuilder.DropTable(
                name: "custom_trigger_rules");
        }
    }
}
