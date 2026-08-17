using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSummaryProjectionAndProcessedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderSummaryProjections",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GrandTotalAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    GrandTotalCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CausationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProjectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSummaryProjections", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.Key, x.Handler });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSummaryProjections_CustomerId_CreatedAtUtc",
                table: "OrderSummaryProjections",
                columns: new[] { "CustomerId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderSummaryProjections");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");
        }
    }
}
