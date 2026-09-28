using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nopds.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "authors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    search_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    lang_code = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "libraries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    root_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    extensions = table.Column<string[]>(type: "text[]", nullable: false),
                    scan_zip = table.Column<bool>(type: "boolean", nullable: false),
                    zip_codepage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    inpx_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    inpx_skip_unchanged = table.Column<bool>(type: "boolean", nullable: false),
                    inpx_test_zip = table.Column<bool>(type: "boolean", nullable: false),
                    inpx_test_files = table.Column<bool>(type: "boolean", nullable: false),
                    watch_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    scan_cron = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    delete_logical = table.Column<bool>(type: "boolean", nullable: false),
                    hash_content = table.Column<bool>(type: "boolean", nullable: false),
                    last_scan_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_scan_finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_scan_summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_libraries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    search_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    lang_code = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    feed_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allowed_library_ids = table.Column<int[]>(type: "integer[]", nullable: true),
                    hide_duplicates = table.Column<bool>(type: "boolean", nullable: false),
                    ui_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    telegram_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kosync_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalogs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    library_id = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    mtime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_catalogs_catalogs_parent_id",
                        column: x => x.parent_id,
                        principalTable: "catalogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_catalogs_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "koreader_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    progress = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    percentage = table.Column<double>(type: "double precision", nullable: false),
                    device = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_koreader_progress", x => new { x.user_id, x.document });
                    table.ForeignKey(
                        name: "fk_koreader_progress_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "books",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    library_id = table.Column<int>(type: "integer", nullable: false),
                    catalog_id = table.Column<long>(type: "bigint", nullable: false),
                    container = table.Column<int>(type: "integer", nullable: false),
                    rel_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    entry_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    file_mtime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    search_title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    annotation = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    doc_date = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    lang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    lang_code = table.Column<int>(type: "integer", nullable: false),
                    dup_group_key = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<long>(type: "bigint", nullable: true),
                    koreader_hash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    cover = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_books", x => x.id);
                    table.ForeignKey(
                        name: "fk_books_catalogs_catalog_id",
                        column: x => x.catalog_id,
                        principalTable: "catalogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_books_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_authors",
                columns: table => new
                {
                    book_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book_authors", x => new { x.book_id, x.author_id });
                    table.ForeignKey(
                        name: "fk_book_authors_authors_author_id",
                        column: x => x.author_id,
                        principalTable: "authors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_book_authors_books_book_id",
                        column: x => x.book_id,
                        principalTable: "books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_genres",
                columns: table => new
                {
                    book_id = table.Column<long>(type: "bigint", nullable: false),
                    genre_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book_genres", x => new { x.book_id, x.genre_id });
                    table.ForeignKey(
                        name: "fk_book_genres_books_book_id",
                        column: x => x.book_id,
                        principalTable: "books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_book_genres_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_series",
                columns: table => new
                {
                    book_id = table.Column<long>(type: "bigint", nullable: false),
                    series_id = table.Column<long>(type: "bigint", nullable: false),
                    ser_no = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book_series", x => new { x.book_id, x.series_id });
                    table.ForeignKey(
                        name: "fk_book_series_books_book_id",
                        column: x => x.book_id,
                        principalTable: "books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_book_series_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reading_states",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<long>(type: "bigint", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    progress = table.Column<double>(type: "double precision", nullable: false),
                    finished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reading_states", x => new { x.user_id, x.book_id });
                    table.ForeignKey(
                        name: "fk_reading_states_books_book_id",
                        column: x => x.book_id,
                        principalTable: "books",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reading_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authors_lang_code_search_name",
                table: "authors",
                columns: new[] { "lang_code", "search_name" });

            migrationBuilder.CreateIndex(
                name: "ix_authors_search_name",
                table: "authors",
                column: "search_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_search_name_trgm",
                table: "authors",
                column: "search_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_book_authors_author_id",
                table: "book_authors",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_book_genres_genre_id",
                table: "book_genres",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_book_series_series_id",
                table: "book_series",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_books_catalog_id",
                table: "books",
                column: "catalog_id");

            migrationBuilder.CreateIndex(
                name: "ix_books_content_hash",
                table: "books",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_books_dup_group_key",
                table: "books",
                column: "dup_group_key");

            migrationBuilder.CreateIndex(
                name: "ix_books_koreader_hash",
                table: "books",
                column: "koreader_hash");

            migrationBuilder.CreateIndex(
                name: "ix_books_library_id_lang_code_search_title",
                table: "books",
                columns: new[] { "library_id", "lang_code", "search_title" });

            migrationBuilder.CreateIndex(
                name: "ix_books_library_id_rel_path_entry_name",
                table: "books",
                columns: new[] { "library_id", "rel_path", "entry_name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_books_registered_at",
                table: "books",
                column: "registered_at");

            migrationBuilder.CreateIndex(
                name: "ix_books_search_title_trgm",
                table: "books",
                column: "search_title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogs_library_id_path",
                table: "catalogs",
                columns: new[] { "library_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogs_parent_id",
                table: "catalogs",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_genres_code",
                table: "genres",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_genres_section",
                table: "genres",
                column: "section");

            migrationBuilder.CreateIndex(
                name: "ix_libraries_name",
                table: "libraries",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reading_states_book_id",
                table: "reading_states",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_reading_states_user_id_last_opened_at",
                table: "reading_states",
                columns: new[] { "user_id", "last_opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_series_lang_code_search_name",
                table: "series",
                columns: new[] { "lang_code", "search_name" });

            migrationBuilder.CreateIndex(
                name: "ix_series_search_name",
                table: "series",
                column: "search_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_search_name_trgm",
                table: "series",
                column: "search_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_users_feed_token",
                table: "users",
                column: "feed_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_username",
                table: "users",
                column: "telegram_username");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_authors");

            migrationBuilder.DropTable(
                name: "book_genres");

            migrationBuilder.DropTable(
                name: "book_series");

            migrationBuilder.DropTable(
                name: "koreader_progress");

            migrationBuilder.DropTable(
                name: "reading_states");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "authors");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "series");

            migrationBuilder.DropTable(
                name: "books");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "catalogs");

            migrationBuilder.DropTable(
                name: "libraries");
        }
    }
}
