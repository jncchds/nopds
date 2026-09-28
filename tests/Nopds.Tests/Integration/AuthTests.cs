using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nopds.Tests.Integration;

[Collection(AppCollection.Name)]
public class AuthTests(AppFixture app)
{
    [Fact]
    public async Task Api_requires_token_and_rejects_bad_password()
    {
        var client = app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/books")).StatusCode);
        var bad = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = AppFixture.AdminUser, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_family()
    {
        var client = app.Factory.CreateClient(new() { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = AppFixture.AdminUser, password = AppFixture.AdminPassword });
        var first = Cookie(login);

        var r1 = await Refresh(client, first);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var second = Cookie(r1);
        Assert.NotEqual(first, second);

        // Reusing the rotated token is treated as theft: it fails and revokes the newer token too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, second)).StatusCode);
    }

    [Fact]
    public async Task Kosync_round_trip()
    {
        var admin = await app.AdminClientAsync();
        (await admin.PutAsJsonAsync("/api/v1/me/kosync", new { password = "sync-secret" })).EnsureSuccessStatusCode();

        var ko = app.Factory.CreateClient();
        ko.DefaultRequestHeaders.Add("x-auth-user", AppFixture.AdminUser);
        ko.DefaultRequestHeaders.Add("x-auth-key", Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("sync-secret"))));
        Assert.Equal(HttpStatusCode.OK, (await ko.GetAsync("/kosync/users/auth")).StatusCode);

        var put = await ko.PutAsJsonAsync("/kosync/syncs/progress", new { document = "0123456789abcdef0123456789abcdef", progress = "/body/DocFragment[3]", percentage = 0.42, device = "test", device_id = "d1" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var got = await ko.GetFromJsonAsync<JsonElement>("/kosync/syncs/progress/0123456789abcdef0123456789abcdef", AppFixture.Json);
        Assert.Equal(0.42, got.GetProperty("percentage").GetDouble(), 3);

        var wrong = app.Factory.CreateClient();
        wrong.DefaultRequestHeaders.Add("x-auth-user", AppFixture.AdminUser);
        wrong.DefaultRequestHeaders.Add("x-auth-key", "00000000000000000000000000000000");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/kosync/users/auth")).StatusCode);
    }

    private static string Cookie(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("nopds_rt=", StringComparison.Ordinal)).Split(';')[0];

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", cookie);
        return client.SendAsync(req);
    }
}
