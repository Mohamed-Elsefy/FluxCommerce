using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantUnitPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPriceAmount",
                table: "Variants",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UnitPriceCurrency",
                table: "Variants",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE [Variants]
                SET [UnitPriceAmount] = 49.99, [UnitPriceCurrency] = 'USD'
                WHERE [UnitPriceCurrency] = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitPriceAmount",
                table: "Variants");

            migrationBuilder.DropColumn(
                name: "UnitPriceCurrency",
                table: "Variants");
        }
    }
}
