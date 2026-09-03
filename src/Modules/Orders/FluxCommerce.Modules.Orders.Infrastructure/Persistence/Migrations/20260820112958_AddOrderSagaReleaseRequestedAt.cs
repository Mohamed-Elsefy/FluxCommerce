using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSagaReleaseRequestedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleaseRequestedAtUtc",
                table: "OrderSagas",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseRequestedAtUtc",
                table: "OrderSagas");
        }
    }
}
