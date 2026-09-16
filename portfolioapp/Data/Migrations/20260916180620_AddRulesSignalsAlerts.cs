using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portfolioapp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRulesSignalsAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RuleType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Threshold = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Signals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WatchRuleId = table.Column<int>(type: "int", nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    FactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Signals_WatchRules_WatchRuleId",
                        column: x => x.WatchRuleId,
                        principalTable: "WatchRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SignalId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Signals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "Signals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SignalId",
                table: "Alerts",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Symbol_WatchRuleId_Status",
                table: "Signals",
                columns: new[] { "Symbol", "WatchRuleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Signals_WatchRuleId",
                table: "Signals",
                column: "WatchRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchRules_Symbol_IsEnabled",
                table: "WatchRules",
                columns: new[] { "Symbol", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "Signals");

            migrationBuilder.DropTable(
                name: "WatchRules");
        }
    }
}
