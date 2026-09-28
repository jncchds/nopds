using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Identity;

namespace Nopds.Infrastructure.Data;

public class NopdsDbContext(DbContextOptions<NopdsDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Catalog> Catalogs => Set<Catalog>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<BookGenre> BookGenres => Set<BookGenre>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<BookSeries> BookSeries => Set<BookSeries>();
    public DbSet<ReadingState> ReadingStates => Set<ReadingState>();
    public DbSet<KoreaderProgress> KoreaderProgress => Set<KoreaderProgress>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasPostgresExtension("pg_trgm");

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.FeedToken).IsUnique();
            e.HasIndex(u => u.TelegramUsername);
            e.Property(u => u.FeedToken).HasMaxLength(64);
            e.Property(u => u.UiLanguage).HasMaxLength(8);
            e.Property(u => u.TelegramUsername).HasMaxLength(64);
            e.Property(u => u.KosyncKeyHash).HasMaxLength(64);
        });
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");

        b.Entity<Library>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(128);
            e.Property(l => l.RootPath).HasMaxLength(1024);
            e.Property(l => l.ZipCodepage).HasMaxLength(16);
            e.Property(l => l.ScanCron).HasMaxLength(64);
            e.HasIndex(l => l.Name).IsUnique();
        });

        b.Entity<Catalog>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(512);
            e.Property(c => c.Path).HasMaxLength(2048);
            e.HasIndex(c => new { c.LibraryId, c.Path }).IsUnique();
            e.HasIndex(c => c.ParentId);
            e.HasOne(c => c.Parent).WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Library).WithMany().HasForeignKey(c => c.LibraryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Book>(e =>
        {
            e.Property(x => x.RelPath).HasMaxLength(2048);
            e.Property(x => x.EntryName).HasMaxLength(1024);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.Format).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.SearchTitle).HasMaxLength(512);
            e.Property(x => x.Annotation).HasMaxLength(10000);
            e.Property(x => x.DocDate).HasMaxLength(32);
            e.Property(x => x.Lang).HasMaxLength(16);
            e.Property(x => x.KoreaderHash).HasMaxLength(32);

            e.HasIndex(x => new { x.LibraryId, x.RelPath, x.EntryName }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => x.CatalogId);
            e.HasIndex(x => new { x.LibraryId, x.LangCode, x.SearchTitle });
            e.HasIndex(x => x.SearchTitle, "ix_books_search_title_trgm").HasDatabaseName("ix_books_search_title_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.DupGroupKey);
            e.HasIndex(x => x.ContentHash);
            e.HasIndex(x => x.KoreaderHash);
            e.HasIndex(x => x.RegisteredAt);

            e.HasOne(x => x.Library).WithMany().HasForeignKey(x => x.LibraryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Catalog).WithMany().HasForeignKey(x => x.CatalogId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Author>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(256);
            e.Property(x => x.SearchName).HasMaxLength(256);
            e.HasIndex(x => x.SearchName, "ix_authors_search_name").IsUnique();
            e.HasIndex(x => x.SearchName, "ix_authors_search_name_trgm").HasDatabaseName("ix_authors_search_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => new { x.LangCode, x.SearchName });
        });

        b.Entity<BookAuthor>(e =>
        {
            e.HasKey(x => new { x.BookId, x.AuthorId });
            e.HasIndex(x => x.AuthorId);
            e.HasOne(x => x.Book).WithMany(x => x.Authors).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany(x => x.Books).HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Genre>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Section).HasMaxLength(32);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Section);
        });

        b.Entity<BookGenre>(e =>
        {
            e.HasKey(x => new { x.BookId, x.GenreId });
            e.HasIndex(x => x.GenreId);
            e.HasOne(x => x.Book).WithMany(x => x.Genres).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Genre).WithMany(x => x.Books).HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Series>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.SearchName).HasMaxLength(256);
            e.HasIndex(x => x.SearchName, "ix_series_search_name").IsUnique();
            e.HasIndex(x => x.SearchName, "ix_series_search_name_trgm").HasDatabaseName("ix_series_search_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => new { x.LangCode, x.SearchName });
        });

        b.Entity<BookSeries>(e =>
        {
            e.HasKey(x => new { x.BookId, x.SeriesId });
            e.HasIndex(x => x.SeriesId);
            e.HasOne(x => x.Book).WithMany(x => x.Series).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Series).WithMany(x => x.Books).HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReadingState>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BookId });
            e.HasIndex(x => new { x.UserId, x.LastOpenedAt });
            e.Property(x => x.Location).HasMaxLength(2048);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<KoreaderProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Document });
            e.Property(x => x.Document).HasMaxLength(64);
            e.Property(x => x.Progress).HasMaxLength(2048);
            e.Property(x => x.Device).HasMaxLength(128);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.ReplacedByHash).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
    }
}
