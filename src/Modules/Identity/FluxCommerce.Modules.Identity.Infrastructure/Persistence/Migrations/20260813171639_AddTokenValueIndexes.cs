using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxCommerce.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenValueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Value",
                table: "RefreshTokens",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_Value",
                table: "PasswordResetTokens",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_Value",
                table: "EmailVerificationTokens",
                column: "Value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Value",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_Value",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_EmailVerificationTokens_Value",
                table: "EmailVerificationTokens");
        }
    }
}
