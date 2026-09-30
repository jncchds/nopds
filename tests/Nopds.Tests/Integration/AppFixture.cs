using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Nopds.Tests.Integration;

/// <summary>Runs the whole app against a throwaway PostgreSQL container with a sample library on disk.</summary>
public class AppFixture : IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "admin-pass-123";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nopds-it-" + Guid.NewGuid().ToString("N")[..8]);
    public string Books => Path.Combine(Root, "books");
    public WebApplicationFactory<Program> Factory => _factory!;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        CreateLibrary();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Nopds", _db.GetConnectionString());
            b.UseSetting("Nopds:DataDir", Path.Combine(Root, "data"));
            b.UseSetting("Nopds:CacheDir", Path.Combine(Root, "data", "cache"));
            b.UseSetting("Nopds:AdminUser", AdminUser);
            b.UseSetting("Nopds:AdminPassword", AdminPassword);
            Configure(b);
        });
        _ = _factory.Server;
    }

    /// <summary>Extra host settings for fixtures that need their own app instance.</summary>
    protected virtual void Configure(IWebHostBuilder builder)
    {
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _db.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void CreateLibrary()
    {
        Directory.CreateDirectory(Path.Combine(Books, "plain"));
        Directory.CreateDirectory(Path.Combine(Books, "zips"));
        Directory.CreateDirectory(Path.Combine(Books, "inpx"));
        foreach (var f in new[] { "262001.fb2", "mirer.epub", "robin_cook.mobi", "badfile.fb2" })
        {
            File.Copy(TestFiles.PathOf(f), Path.Combine(Books, "plain", f));
        }

        File.Copy(TestFiles.PathOf("books.zip"), Path.Combine(Books, "zips", "books.zip"));
        File.Copy(TestFiles.PathOf("badfile.zip"), Path.Combine(Books, "zips", "badfile.zip"));
        File.Copy(TestFiles.PathOf("books.zip"), Path.Combine(Books, "inpx", "fb2-001.zip"));

        // INPX with two live records (one duplicates a ZIP book), a repeated record and one deleted record.
        const char sep = '\u0004';
        string Line(params string[] f) => string.Join(sep, f);
        var inp = string.Join("\r\n",
            Line("Логинов,Святослав,:", "prose_rus_classic:", "Любовь в жизни Обломова", "Рассказы", "1", "539603", "15194", "1", "0", "fb2", "2014-09-15", "ru"),
            Line("Doe,John,:Smith,Ann,:", "sf:", "Test INPX Book", "", "", "539485", "12293", "2", "0", "fb2", "2015-01-01", "en"),
            Line("Doe,John,:Smith,Ann,:", "sf:", "Test INPX Book", "", "", "539485", "12293", "2", "0", "fb2", "2015-01-01", "en"),
            Line("Deleted,Guy,:", "sf:", "Deleted", "", "", "539273", "1", "3", "1", "fb2", "2015-01-01", "en")) + "\r\n";
        using var zip = ZipFile.Open(Path.Combine(Books, "inpx", "lib.inpx"), ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry("fb2-001.inp").Open(), new UTF8Encoding(false));
        w.Write(inp);
    }

    public Task<HttpClient> AdminClientAsync() => ClientAsync(AdminUser, AdminPassword);

    public async Task<HttpClient> ClientAsync(string userName, string password)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password });
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Starts a scan and waits for it to finish; returns the final status object.</summary>
    public static async Task<JsonElement> ScanAsync(HttpClient admin, int libraryId, string? path = null)
    {
        var started = DateTimeOffset.UtcNow;
        (await admin.PostAsync($"/api/v1/admin/libraries/{libraryId}/scan{(path is null ? "" : "?path=" + Uri.EscapeDataString(path))}", null)).EnsureSuccessStatusCode();
        for (var i = 0; i < 300; i++)
        {
            await Task.Delay(100);
            var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/scan", Json);
            foreach (var s in list.EnumerateArray())
            {
                if (s.GetProperty("libraryId").GetInt32() == libraryId
                    && s.GetProperty("state").GetString() is "completed" or "failed" or "cancelled"
                    && s.TryGetProperty("finishedAt", out var f) && f.GetDateTimeOffset() >= started)
                {
                    return s;
                }
            }
        }

        throw new TimeoutException("Scan did not finish");
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "app";
}
