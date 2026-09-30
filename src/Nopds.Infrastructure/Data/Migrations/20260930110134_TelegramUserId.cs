using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nopds.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TelegramUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_user_id",
                table: "users",
                column: "telegram_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_telegram_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_user_id",
                table: "users");
        }
    }
}
