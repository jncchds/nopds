using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nopds.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Uploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_uploads",
                table: "libraries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "uploads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    library_id = table.Column<int>(type: "integer", nullable: false),
                    rel_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    original_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uploads", x => x.id);
                    table.ForeignKey(
                        name: "fk_uploads_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_uploads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_uploads_library_id_rel_path",
                table: "uploads",
                columns: new[] { "library_id", "rel_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uploads_user_id",
                table: "uploads",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uploads");

            migrationBuilder.DropColumn(
                name: "is_uploads",
                table: "libraries");
        }
    }
}
