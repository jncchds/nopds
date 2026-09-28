using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;
using Nopds.Web.Endpoints;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Auth;

public enum SsoOutcome
{
    SignedIn,
    Pending,
    Locked,
    Error,
}

/// <summary>
/// Single sign-on through an OpenID Connect provider (Authentik). The OIDC handler only runs the
/// code flow; on success we find or create the local account and hand out the usual refresh cookie,
/// so the SPA continues exactly as after a password login.
/// </summary>
public static class Sso
{
    public const string SchemeName = "oidc";
    public const string CallbackPath = "/signin-oidc";

    public static AuthenticationBuilder AddNopdsSso(this AuthenticationBuilder builder, OidcOptions oidc)
    {
        if (!oidc.Enabled)
        {
            return builder;
        }

        return builder.AddOpenIdConnect(SchemeName, oidc.DisplayName, o =>
        {
            o.Authority = oidc.Authority;
            o.ClientId = oidc.ClientId;
            o.ClientSecret = oidc.ClientSecret;
            o.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.UsePkce = true;
            o.CallbackPath = CallbackPath;
            o.MapInboundClaims = false;
            o.GetClaimsFromUserInfoEndpoint = true;
            o.SaveTokens = false;
            o.Scope.Clear();
            foreach (var scope in oidc.Scopes)
            {
                o.Scope.Add(scope);
            }

            o.Events = new OpenIdConnectEvents
            {
                OnTicketReceived = CompleteAsync,
                OnRemoteFailure = ctx =>
                {
                    ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Sso))
                        .LogWarning(ctx.Failure, "Single sign-on failed");
                    ctx.Response.Redirect(LoginPage(SsoOutcome.Error));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });
    }

    /// <summary>Starts the provider login; only local return URLs are accepted.</summary>
    public static IResult Challenge(string? returnUrl, IOptions<NopdsOptions> options) =>
        options.Value.Oidc.Enabled
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = LocalUrl(returnUrl) }, [SchemeName])
            : Results.NotFound();

    private static async Task CompleteAsync(TicketReceivedContext ctx)
    {
        var http = ctx.HttpContext;
        var accounts = http.RequestServices.GetRequiredService<SsoAccounts>();
        var (outcome, user) = await accounts.ResolveAsync(ctx.Principal!);
        if (outcome == SsoOutcome.SignedIn)
        {
            var issued = await http.RequestServices.GetRequiredService<TokenService>().IssueAsync(user!, http.Request.Headers.UserAgent, http.RequestAborted);
            AuthEndpoints.SetRefreshCookie(http, issued);
            ctx.Response.Redirect(LocalUrl(ctx.ReturnUri));
        }
        else
        {
            ctx.Response.Redirect(LoginPage(outcome));
        }

        // Skips the sign-in scheme: the session is our refresh cookie, not an auth cookie.
        ctx.HandleResponse();
    }

    private static string LoginPage(SsoOutcome outcome) => "/login?sso=" + outcome.ToString().ToLowerInvariant();

    private static string LocalUrl(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";
}

/// <summary>Maps an OIDC identity to a local account, creating it on first sign-in.</summary>
public sealed class SsoAccounts(UserManager<AppUser> users, SettingsStore settings, IOptions<NopdsOptions> options, TimeProvider clock, ILogger<SsoAccounts> log)
{
    private readonly OidcOptions _oidc = options.Value.Oidc;

    public async Task<(SsoOutcome Outcome, AppUser? User)> ResolveAsync(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject))
        {
            log.LogWarning("Single sign-on response has no subject claim");
            return (SsoOutcome.Error, null);
        }

        var user = await users.FindByLoginAsync(Sso.SchemeName, subject);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = await UniqueUserNameAsync(principal),
                Email = principal.FindFirstValue("email"),
                IsApproved = !settings.Current.Sso.RequireApproval,
            };
            var created = await users.CreateAsync(user);
            if (created.Succeeded)
            {
                created = await users.AddLoginAsync(user, new UserLoginInfo(Sso.SchemeName, subject, _oidc.DisplayName));
            }

            if (!created.Succeeded)
            {
                log.LogError("Could not create single sign-on user {UserName}: {Errors}", user.UserName, string.Join("; ", created.Errors.Select(e => e.Description)));
                return (SsoOutcome.Error, null);
            }

            log.LogInformation("Created user {UserName} from single sign-on ({State})", user.UserName, user.IsApproved ? "approved" : "pending approval");
        }

        if (!user.IsApproved)
        {
            return (SsoOutcome.Pending, user);
        }

        return user.CanSignIn(clock.GetUtcNow()) ? (SsoOutcome.SignedIn, user) : (SsoOutcome.Locked, user);
    }

    private async Task<string> UniqueUserNameAsync(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("preferred_username")
                  ?? principal.FindFirstValue("email")?.Split('@')[0]
                  ?? principal.FindFirstValue("name")
                  ?? "user";
        var allowed = users.Options.User.AllowedUserNameCharacters;
        var sb = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            sb.Append(string.IsNullOrEmpty(allowed) || allowed.Contains(ch) ? ch : '_');
        }

        var name = sb.Length > 0 ? sb.ToString() : "user";
        if (name.Length > 60)
        {
            name = name[..60];
        }

        // Never attach to an existing local account by name: that would let the provider take it over.
        var candidate = name;
        for (var i = 2; await users.FindByNameAsync(candidate) is not null; i++)
        {
            candidate = name + i;
        }

        return candidate;
    }
}
