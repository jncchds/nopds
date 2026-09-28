using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nopds.Domain.Text;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;
using Nopds.Web.Auth;

namespace Nopds.Web.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshCookie = "nopds_rt";
    public const string RateLimitPolicy = "auth";

    public sealed record LoginRequest(string UserName, string Password);

    public sealed record UserDto(Guid Id, string UserName, bool IsAdmin, string? UiLanguage, bool HideDuplicates, int[]? AllowedLibraryIds, string? TelegramUsername, bool KosyncConfigured);

    public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

    public sealed record ProfileUpdate(string? UiLanguage, bool? HideDuplicates, string? TelegramUsername);

    public sealed record PasswordChange(string CurrentPassword, string NewPassword);

    public sealed record KosyncKey(string Password);

    public static UserDto ToDto(this AppUser u) =>
        new(u.Id, u.UserName ?? "", u.IsAdmin, u.UiLanguage, u.HideDuplicates, u.AllowedLibraryIds, u.TelegramUsername, u.KosyncKeyHash is not null);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/login", async (LoginRequest req, UserManager<AppUser> users, TokenService tokens, HttpContext http, CancellationToken ct) =>
        {
            var user = await users.FindByNameAsync(req.UserName);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (await users.IsLockedOutAsync(user))
            {
                return Results.Problem("Account is temporarily locked.", statusCode: StatusCodes.Status423Locked);
            }

            if (!await users.CheckPasswordAsync(user, req.Password))
            {
                await users.AccessFailedAsync(user);
                return Results.Unauthorized();
            }

            await users.ResetAccessFailedCountAsync(user);
            var issued = await tokens.IssueAsync(user, http.Request.Headers.UserAgent, ct);
            SetRefreshCookie(http, issued);
            return Results.Ok(new AuthResponse(issued.AccessToken, issued.AccessExpires, user.ToDto()));
        });

        auth.MapPost("/refresh", async (TokenService tokens, HttpContext http, CancellationToken ct) =>
        {
            if (!http.Request.Cookies.TryGetValue(RefreshCookie, out var rt) || string.IsNullOrEmpty(rt))
            {
                return Results.Unauthorized();
            }

            var result = await tokens.RefreshAsync(rt, http.Request.Headers.UserAgent, ct);
            if (result is null)
            {
                ClearRefreshCookie(http);
                return Results.Unauthorized();
            }

            SetRefreshCookie(http, result.Value.Tokens);
            return Results.Ok(new AuthResponse(result.Value.Tokens.AccessToken, result.Value.Tokens.AccessExpires, result.Value.User.ToDto()));
        });

        auth.MapPost("/logout", async (TokenService tokens, HttpContext http, CancellationToken ct) =>
        {
            if (http.Request.Cookies.TryGetValue(RefreshCookie, out var rt) && !string.IsNullOrEmpty(rt))
            {
                await tokens.RevokeAsync(rt, ct);
            }

            ClearRefreshCookie(http);
            return Results.NoContent();
        });

        var me = api.MapGroup("/me").RequireAuthorization(Policies.User);

        me.MapGet("", async (CurrentUser cu, NopdsDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cu.Id, ct) is { } u ? Results.Ok(u.ToDto()) : Results.Unauthorized());

        me.MapPut("", async (ProfileUpdate req, CurrentUser cu, NopdsDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.FirstAsync(x => x.Id == cu.Id, ct);
            if (req.UiLanguage is not null)
            {
                u.UiLanguage = req.UiLanguage.Length == 0 ? null : UiLanguages.Match(req.UiLanguage);
            }

            if (req.HideDuplicates is { } hide)
            {
                u.HideDuplicates = hide;
            }

            if (req.TelegramUsername is not null)
            {
                var tg = req.TelegramUsername.Trim().TrimStart('@');
                u.TelegramUsername = tg.Length == 0 ? null : tg;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(u.ToDto());
        });

        me.MapPost("/password", async (PasswordChange req, CurrentUser cu, UserManager<AppUser> users, TokenService tokens, CancellationToken ct) =>
        {
            var u = await users.FindByIdAsync(cu.Id!.Value.ToString());
            if (u is null)
            {
                return Results.Unauthorized();
            }

            var r = await users.ChangePasswordAsync(u, req.CurrentPassword, req.NewPassword);
            if (!r.Succeeded)
            {
                return Results.ValidationProblem(r.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            await tokens.RevokeAllAsync(u.Id, ct);
            return Results.NoContent();
        });

        me.MapGet("/feed-token", async (CurrentUser cu, NopdsDbContext db, CancellationToken ct) =>
            Results.Ok(new { token = await db.Users.Where(u => u.Id == cu.Id).Select(u => u.FeedToken).FirstAsync(ct) }));

        me.MapPost("/feed-token", async (CurrentUser cu, NopdsDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.FirstAsync(x => x.Id == cu.Id, ct);
            u.FeedToken = AppUser.NewToken();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { token = u.FeedToken });
        });

        me.MapPut("/kosync", async (KosyncKey req, CurrentUser cu, NopdsDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.FirstAsync(x => x.Id == cu.Id, ct);
            u.KosyncKeyHash = string.IsNullOrEmpty(req.Password) ? null : KosyncKeys.HashPassword(req.Password);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void SetRefreshCookie(HttpContext http, IssuedTokens t) =>
        http.Response.Cookies.Append(RefreshCookie, t.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = t.RefreshExpires,
            IsEssential = true,
        });

    private static void ClearRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/v1/auth" });
}

/// <summary>KOReader sends md5(password) as its key; we store sha256 of that.</summary>
public static class KosyncKeys
{
    public static string HashPassword(string password) =>
        HashKey(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(password))));

    public static string HashKey(string md5Key) => TokenService.Hash(md5Key.ToLowerInvariant());
}
