using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portfolioapp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioAndWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceDownloadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowsParsed = table.Column<int>(type: "int", nullable: false),
                    PositionsImported = table.Column<int>(type: "int", nullable: false),
                    RowsSkipped = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchlistSymbols",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistSymbols", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holdings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    AverageCostBasis = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CostBasisTotal = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CurrentValue = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LastPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PositionType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsMonitorable = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastImportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holdings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holdings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Holdings_PortfolioImports_LastImportId",
                        column: x => x.LastImportId,
                        principalTable: "PortfolioImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Name",
                table: "Accounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_AccountId_Symbol",
                table: "Holdings",
                columns: new[] { "AccountId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_LastImportId",
                table: "Holdings",
                column: "LastImportId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioImports_ImportedAt",
                table: "PortfolioImports",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistSymbols_Symbol",
                table: "WatchlistSymbols",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holdings");

            migrationBuilder.DropTable(
                name: "WatchlistSymbols");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "PortfolioImports");
        }
    }
}
