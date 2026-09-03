using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentToOrderSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                table: "OrderSagas",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPaymentIntentId",
                table: "OrderSagas",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentFailureReason",
                table: "OrderSagas",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentRequestedAtUtc",
                table: "OrderSagas",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "ProviderPaymentIntentId",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "PaymentFailureReason",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "PaymentRequestedAtUtc",
                table: "OrderSagas");
        }
    }
}
