using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Nopds.Tests.Integration;

/// <summary>Own app instance with an upload library, so uploaded books do not change the counts other tests expect.</summary>
public sealed class UploadFixture : AppFixture
{
    public string Uploads => Path.Combine(Root, "uploads");

    protected override void Configure(IWebHostBuilder builder) => builder.UseSetting("Nopds:UploadPath", Uploads);
}

public class UploadTests(UploadFixture app) : IClassFixture<UploadFixture>
{
    [Fact]
    public async Task Users_upload_books_and_private_ones_stay_with_their_owner()
    {
        var admin = await app.AdminClientAsync();
        var config = await admin.GetFromJsonAsync<JsonElement>("/api/v1/config", AppFixture.Json);
        var libraryId = config.GetProperty("uploads").GetProperty("libraryId").GetInt32();
        Assert.True(Directory.Exists(app.Uploads));

        // Bob has no library access at all, yet may use the upload library.
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new { userName = "alice", password = "alice-pass" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/v1/admin/users", new { userName = "bob", password = "bob-pass-1", allLibraries = false, allowedLibraryIds = Array.Empty<int>() }))
            .EnsureSuccessStatusCode();
        var alice = await app.ClientAsync("alice", "alice-pass");
        var bob = await app.ClientAsync("bob", "bob-pass-1");

        var bad = await UploadAsync(alice, "notes.exe", isPrivate: false);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var alicesUpload = await ReadUploadAsync(await UploadAsync(alice, "262001.fb2", isPrivate: true));
        var bobsUpload = await ReadUploadAsync(await UploadAsync(bob, "mirer.epub", isPrivate: null));
        Assert.True(alicesUpload.GetProperty("isPrivate").GetBoolean());
        Assert.False(bobsUpload.GetProperty("isPrivate").GetBoolean());

        var aliceBook = await WaitForBookAsync(alice, alicesUpload.GetProperty("id").GetInt64());
        var bobBook = await WaitForBookAsync(bob, bobsUpload.GetProperty("id").GetInt64());

        // The private book is invisible to Bob everywhere; Alice and admins see both.
        Assert.Equal([bobBook], await BookIdsAsync(bob, libraryId));
        Assert.Equal([aliceBook, bobBook], (await BookIdsAsync(alice, libraryId)).Order());
        Assert.Equal([aliceBook, bobBook], (await BookIdsAsync(admin, libraryId)).Order());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/books/{aliceBook}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/books/{aliceBook}/download")).StatusCode);
        var libs = await bob.GetFromJsonAsync<JsonElement>("/api/v1/libraries", AppFixture.Json);
        var uploadsLib = Assert.Single(libs.EnumerateArray());
        Assert.Equal(1, uploadsLib.GetProperty("books").GetInt32());

        var details = await alice.GetFromJsonAsync<JsonElement>($"/api/v1/books/{aliceBook}", AppFixture.Json);
        var info = details.GetProperty("book").GetProperty("upload");
        Assert.True(info.GetProperty("mine").GetBoolean());
        Assert.Equal("alice", info.GetProperty("uploadedBy").GetString());

        // Only the owner (or an admin) changes privacy; making it public shows it to Bob.
        var id = alicesUpload.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/v1/uploads/{id}", new { isPrivate = false })).StatusCode);
        (await alice.PutAsJsonAsync($"/api/v1/uploads/{id}", new { isPrivate = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/v1/books/{aliceBook}")).StatusCode);

        // A file copied into the folder by hand becomes a public upload of the first user (the admin).
        File.Copy(TestFiles.PathOf("robin_cook.mobi"), Path.Combine(app.Uploads, "manual.mobi"));
        await AppFixture.ScanAsync(admin, libraryId);
        var adminUploads = await admin.GetFromJsonAsync<JsonElement>("/api/v1/uploads", AppFixture.Json);
        var manual = Assert.Single(adminUploads.EnumerateArray());
        Assert.Equal("manual.mobi", manual.GetProperty("relPath").GetString());
        Assert.Equal(AppFixture.AdminUser, manual.GetProperty("uploadedBy").GetString());
        Assert.False(manual.GetProperty("isPrivate").GetBoolean());
        Assert.Contains(manual.GetProperty("bookId").GetInt64(), await BookIdsAsync(bob, libraryId));

        // A file that disappears is hidden, and its upload record remembers it as missing.
        File.Delete(Path.Combine(app.Uploads, alicesUpload.GetProperty("relPath").GetString()!));
        var scan = await AppFixture.ScanAsync(admin, libraryId);
        Assert.Equal(1, scan.GetProperty("booksDeleted").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/books/{aliceBook}")).StatusCode);
        var mine = await alice.GetFromJsonAsync<JsonElement>("/api/v1/uploads", AppFixture.Json);
        Assert.True(Assert.Single(mine.EnumerateArray()).GetProperty("missing").GetBoolean());

        // The upload library cannot be deleted while it is configured.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/admin/libraries/{libraryId}")).StatusCode);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string file, bool? isPrivate)
    {
        var form = new MultipartFormDataContent();
        var path = TestFiles.PathOf(file) is var p && File.Exists(p) ? p : null;
        form.Add(new ByteArrayContent(path is null ? [1, 2, 3] : File.ReadAllBytes(path)), "files", file);
        if (isPrivate is { } v)
        {
            form.Add(new StringContent(v ? "true" : "false"), "isPrivate");
        }

        return client.PostAsync("/api/v1/uploads", form);
    }

    private static async Task<JsonElement> ReadUploadAsync(HttpResponseMessage res)
    {
        res.EnsureSuccessStatusCode();
        return Assert.Single((await res.Content.ReadFromJsonAsync<JsonElement>(AppFixture.Json)).EnumerateArray());
    }

    private static async Task<long> WaitForBookAsync(HttpClient client, long uploadId)
    {
        for (var i = 0; i < 300; i++)
        {
            var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/uploads", AppFixture.Json);
            var u = list.EnumerateArray().Single(x => x.GetProperty("id").GetInt64() == uploadId);
            if (u.TryGetProperty("bookId", out var b) && b.ValueKind == JsonValueKind.Number)
            {
                return b.GetInt64();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Upload was not scanned");
    }

    private static async Task<long[]> BookIdsAsync(HttpClient client, int libraryId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/books?library={libraryId}&pageSize=100", AppFixture.Json);
        return page.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetInt64()).ToArray();
    }
}
