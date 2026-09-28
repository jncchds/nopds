using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;

namespace Nopds.Tests.Integration;

[Collection(AppCollection.Name)]
public class SsoTests(AppFixture app)
{
    [Fact]
    public async Task New_user_is_pending_until_approved()
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var accounts = Accounts(scope.ServiceProvider);
        var sub = Guid.NewGuid().ToString("N");

        var (first, user) = await accounts.ResolveAsync(Principal(sub, "reader one"));
        Assert.Equal(SsoOutcome.Pending, first);
        Assert.Equal("reader_one", user!.UserName);
        Assert.False(user.IsApproved);

        var admin = await app.AdminClientAsync();
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users", AppFixture.Json);
        var dto = list.EnumerateArray().First(u => u.GetProperty("id").GetGuid() == user.Id);
        Assert.False(dto.GetProperty("approved").GetBoolean());
        Assert.Equal("Authentik", dto.GetProperty("sso").GetString());

        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}", new { approved = true })).EnsureSuccessStatusCode();

        await using var scope2 = app.Factory.Services.CreateAsyncScope();
        var (second, same) = await Accounts(scope2.ServiceProvider).ResolveAsync(Principal(sub, "renamed"));
        Assert.Equal(SsoOutcome.SignedIn, second);
        Assert.Equal(user.Id, same!.Id);
    }

    [Fact]
    public async Task Approval_can_be_disabled_and_local_accounts_are_never_taken_over()
    {
        var settings = app.Factory.Services.GetRequiredService<SettingsStore>();
        var original = settings.Current;
        await settings.SaveAsync(original with { Sso = new SsoSettings { RequireApproval = false } });
        try
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            var (outcome, user) = await Accounts(scope.ServiceProvider).ResolveAsync(Principal(Guid.NewGuid().ToString("N"), AppFixture.AdminUser));
            Assert.Equal(SsoOutcome.SignedIn, outcome);
            Assert.False(user!.IsAdmin);
            // Never merged into the existing local account with the same name.
            Assert.Equal(AppFixture.AdminUser + "2", user.UserName);
        }
        finally
        {
            await settings.SaveAsync(original);
        }
    }

    [Fact]
    public async Task Pending_user_cannot_use_password_or_feed_token()
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var (_, user) = await Accounts(scope.ServiceProvider).ResolveAsync(Principal(Guid.NewGuid().ToString("N"), "pending-pw"));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await users.AddPasswordAsync(user!, "secret-123")).Succeeded);

        var client = app.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "pending-pw", password = "secret-123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/opds/t/{user!.FeedToken}/")).StatusCode);
    }

    [Fact]
    public async Task Challenge_redirects_to_provider_only_when_configured()
    {
        var plain = app.Factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await plain.GetAsync("/api/v1/auth/sso")).StatusCode);

        await using var factory = app.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Nopds:Oidc:Authority", "https://idp.test/application/o/nopds/");
            b.UseSetting("Nopds:Oidc:ClientId", "nopds");
            b.UseSetting("Nopds:Oidc:ClientSecret", "secret");
            // No real provider: serve the discovery document from memory.
            b.ConfigureTestServices(s => s.PostConfigure<OpenIdConnectOptions>(Sso.SchemeName, o =>
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
                {
                    Issuer = "https://idp.test/application/o/nopds/",
                    AuthorizationEndpoint = "https://idp.test/application/o/authorize/",
                    TokenEndpoint = "https://idp.test/application/o/token/",
                })));
        });
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var config = await client.GetFromJsonAsync<JsonElement>("/api/v1/config", AppFixture.Json);
        Assert.Equal("Authentik", config.GetProperty("sso").GetString());

        var res = await client.GetAsync("/api/v1/auth/sso?returnUrl=%2Fshelf");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.ToString();
        Assert.StartsWith("https://idp.test/application/o/authorize/", location);
        Assert.Contains("client_id=nopds", location);
        Assert.Contains("code_challenge=", location);
        Assert.Contains(Uri.EscapeDataString("/signin-oidc"), location);
    }

    private static SsoAccounts Accounts(IServiceProvider sp) => new(
        sp.GetRequiredService<UserManager<AppUser>>(),
        sp.GetRequiredService<SettingsStore>(),
        Options.Create(new NopdsOptions { Oidc = new OidcOptions { Authority = "https://idp.test/", ClientId = "nopds" } }),
        TimeProvider.System,
        NullLogger<SsoAccounts>.Instance);

    private static ClaimsPrincipal Principal(string sub, string userName) =>
        new(new ClaimsIdentity(
            [new Claim("sub", sub), new Claim("preferred_username", userName), new Claim("email", userName.Replace(' ', '.') + "@example.com")],
            "oidc"));
}
