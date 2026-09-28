using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Nopds.Tests.Integration;

[Collection(AppCollection.Name)]
public class ScanAndBrowseTests(AppFixture app)
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    [Fact]
    public async Task Library_lifecycle_scan_rescan_delete_restore()
    {
        var admin = await app.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/libraries", new { name = "It", rootPath = app.Books, deleteLogical = true, scanCron = "" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var libId = (await created.Content.ReadFromJsonAsync<JsonElement>(AppFixture.Json)).GetProperty("id").GetInt32();

        // First scan: 3 plain + 3 from ZIP + 2 from INPX; two broken files are reported, not fatal.
        var first = await AppFixture.ScanAsync(admin, libId);
        Assert.Equal("completed", first.GetProperty("state").GetString());
        Assert.Equal(8, first.GetProperty("booksAdded").GetInt32());
        Assert.Equal(2, first.GetProperty("errors").GetInt32());

        // Incremental rescan touches nothing.
        var second = await AppFixture.ScanAsync(admin, libId);
        Assert.Equal(0, second.GetProperty("booksAdded").GetInt32());
        Assert.Equal(8, second.GetProperty("booksSkipped").GetInt32());
        Assert.Equal(2, second.GetProperty("archivesSkipped").GetInt32());

        // Duplicate hiding: the ZIP and INPX copies of one book collapse into one entry with 2 editions.
        var hidden = await admin.GetFromJsonAsync<JsonElement>("/api/v1/books?pageSize=100&total=true", AppFixture.Json);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/books?pageSize=100&total=true&dupes=true", AppFixture.Json);
        Assert.Equal(7, hidden.GetProperty("total").GetInt32());
        Assert.Equal(8, all.GetProperty("total").GetInt32());
        var oblomov = hidden.GetProperty("items").EnumerateArray().Single(b => b.GetProperty("title").GetString() == "Любовь в жизни Обломова");
        Assert.Equal(2, oblomov.GetProperty("editions").GetInt32());

        // Search by Cyrillic substring and author.
        var search = await admin.GetFromJsonAsync<JsonElement>("/api/v1/books?q=" + Uri.EscapeDataString("обломов"), AppFixture.Json);
        Assert.Single(search.GetProperty("items").EnumerateArray());
        var authors = await admin.GetFromJsonAsync<JsonElement>("/api/v1/authors?q=" + Uri.EscapeDataString("пет") + "&match=contains", AppFixture.Json);
        Assert.Empty(authors.GetProperty("items").EnumerateArray());
        var peters = await admin.GetFromJsonAsync<JsonElement>("/api/v1/authors?q=peters", AppFixture.Json);
        Assert.Equal("Peters Ellis", peters.GetProperty("items")[0].GetProperty("name").GetString());

        // Soft delete: remove a file, rescan, book disappears; restore it, book comes back.
        var epub = Path.Combine(app.Books, "plain", "mirer.epub");
        var moved = epub + ".bak";
        File.Move(epub, moved);
        var third = await AppFixture.ScanAsync(admin, libId);
        Assert.Equal(1, third.GetProperty("booksDeleted").GetInt32());
        var afterDelete = await admin.GetFromJsonAsync<JsonElement>("/api/v1/books?pageSize=100&total=true&dupes=true", AppFixture.Json);
        Assert.Equal(7, afterDelete.GetProperty("total").GetInt32());

        File.Move(moved, epub);
        var fourth = await AppFixture.ScanAsync(admin, libId);
        Assert.Equal(1, fourth.GetProperty("booksRestored").GetInt32());

        // Downloads: plain file, zipped, converted, and cover.
        var sparrow = hidden.GetProperty("items").EnumerateArray().Single(b => b.GetProperty("title").GetString() == "The Sanctuary Sparrow").GetProperty("id").GetInt64();
        var dl = await admin.GetAsync($"/api/v1/books/{sparrow}/download");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal(495373, (await dl.Content.ReadAsByteArrayAsync()).Length);
        var epubDl = await admin.GetAsync($"/api/v1/books/{sparrow}/download?format=epub");
        Assert.Equal("application/epub+zip", epubDl.Content.Headers.ContentType?.MediaType);
        var cover = await admin.GetAsync($"/api/v1/books/{sparrow}/thumb");
        Assert.Equal("image/webp", cover.Content.Headers.ContentType?.MediaType);

        // Books inside ZIP and INPX archives are streamed from the archive.
        var inpxBook = hidden.GetProperty("items").EnumerateArray().Single(b => b.GetProperty("title").GetString() == "Test INPX Book").GetProperty("id").GetInt64();
        var inpxDl = await admin.GetAsync($"/api/v1/books/{inpxBook}/download");
        Assert.Equal(HttpStatusCode.OK, inpxDl.StatusCode);
        Assert.Equal(12293, (await inpxDl.Content.ReadAsByteArrayAsync()).Length);

        // OPDS 1.2 with Basic auth, OPDS 2.0 via feed token.
        var opds = app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await opds.GetAsync("/opds/")).StatusCode);
        opds.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AppFixture.AdminUser}:{AppFixture.AdminPassword}")));
        var feed = XDocument.Parse(await opds.GetStringAsync("/opds/search/books/m/" + Uri.EscapeDataString("sparrow") + "/"));
        var entry = Assert.Single(feed.Root!.Elements(Atom + "entry"));
        Assert.Contains(entry.Elements(Atom + "link"), l => (string?)l.Attribute("rel") == "http://opds-spec.org/acquisition/open-access");

        var token = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/feed-token", AppFixture.Json)).GetProperty("token").GetString();
        var anon = app.Factory.CreateClient();
        var v2 = await anon.GetFromJsonAsync<JsonElement>($"/opds/t/{token}/v2/new/", AppFixture.Json);
        Assert.True(v2.GetProperty("publications").GetArrayLength() >= 7);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/opds/t/not-a-token/")).StatusCode);
    }
}
