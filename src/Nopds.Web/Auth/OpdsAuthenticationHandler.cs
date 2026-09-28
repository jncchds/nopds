using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Nopds.Infrastructure.Identity;

namespace Nopds.Web.Auth;

/// <summary>
/// Authentication for e-reader clients: HTTP Basic (username/password) or a per-user feed token
/// embedded in the path (/opds/t/{token}/...). Successful Basic checks are cached briefly because
/// readers send credentials on every request and password hashing is deliberately slow.
/// </summary>
public sealed class OpdsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UserManager<AppUser> users,
    IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Opds";
    public const string FeedTokenRouteKey = "feedToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.RouteValues.TryGetValue(FeedTokenRouteKey, out var tokenValue) && tokenValue is string token && token.Length > 0)
        {
            var user = await cache.GetOrCreateAsync("feedtoken:" + token, async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return await users.Users.AsNoTracking().FirstOrDefaultAsync(u => u.FeedToken == token);
            });
            return user is null || IsLocked(user) ? AuthenticateResult.Fail("Invalid feed token") : Success(user);
        }

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return AuthenticateResult.NoResult();
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Malformed Basic credentials");
        }

        var sep = decoded.IndexOf(':');
        if (sep <= 0)
        {
            return AuthenticateResult.Fail("Malformed Basic credentials");
        }

        var name = decoded[..sep];
        var password = decoded[(sep + 1)..];
        var cacheKey = "basic:" + TokenService.Hash(decoded);
        if (cache.TryGetValue(cacheKey, out AppUser? cached) && cached is not null)
        {
            return Success(cached);
        }

        var found = await users.FindByNameAsync(name);
        if (found is null || IsLocked(found))
        {
            return AuthenticateResult.Fail("Invalid credentials");
        }

        if (!await users.CheckPasswordAsync(found, password))
        {
            await users.AccessFailedAsync(found);
            return AuthenticateResult.Fail("Invalid credentials");
        }

        cache.Set(cacheKey, found, TimeSpan.FromMinutes(5));
        return Success(found);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\".NET OPDS\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }

    private static bool IsLocked(AppUser user) => user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

    private AuthenticateResult Success(AppUser user)
    {
        var identity = new ClaimsIdentity(NopdsClaims.For(user), SchemeName, NopdsClaims.Name, null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
