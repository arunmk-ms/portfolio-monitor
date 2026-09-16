using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portfolioapp.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PreviousClose = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Change = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ChangePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    LatestTradingDay = table.Column<DateOnly>(type: "date", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshots_Symbol_CapturedAt",
                table: "PriceSnapshots",
                columns: new[] { "Symbol", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceSnapshots");
        }
    }
}
