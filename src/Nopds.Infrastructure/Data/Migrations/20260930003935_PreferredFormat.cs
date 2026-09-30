using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nopds.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreferredFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_format",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Existing accounts get the same default as new ones.
            migrationBuilder.Sql("UPDATE users SET preferred_format = 'fb2'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferred_format",
                table: "users");
        }
    }
}
