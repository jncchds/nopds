using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nopds.Domain.Entities;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;
using Nopds.Web.Infrastructure;

namespace Nopds.Web.Auth;

public sealed record IssuedTokens(string AccessToken, DateTimeOffset AccessExpires, string RefreshToken, DateTimeOffset RefreshExpires);

/// <summary>Issues JWT access tokens and rotating refresh tokens (stored hashed).</summary>
public sealed class TokenService(NopdsDbContext db, SigningKeyProvider key, IOptions<NopdsOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _jwt = options.Value.Jwt;

    public string CreateAccessToken(AppUser user, out DateTimeOffset expires)
    {
        var now = clock.GetUtcNow();
        expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Issuer,
            claims: NopdsClaims.For(user),
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key.Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<IssuedTokens> IssueAsync(AppUser user, string? userAgent, CancellationToken ct)
    {
        var access = CreateAccessToken(user, out var accessExpires);
        var (refresh, entity) = NewRefresh(user.Id, userAgent);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new IssuedTokens(access, accessExpires, refresh, entity.ExpiresAt);
    }

    /// <summary>Rotates a refresh token. Reuse of a revoked token revokes the whole token family of that user.</summary>
    public async Task<(AppUser User, IssuedTokens Tokens)?> RefreshAsync(string refreshToken, string? userAgent, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var now = clock.GetUtcNow();
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            return null;
        }

        if (stored.RevokedAt is not null)
        {
            await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return null;
        }

        if (stored.ExpiresAt <= now)
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || (user.LockoutEnd is { } end && end > now))
        {
            return null;
        }

        var (refresh, entity) = NewRefresh(user.Id, userAgent);
        stored.RevokedAt = now;
        stored.ReplacedByHash = entity.TokenHash;
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        var access = CreateAccessToken(user, out var accessExpires);
        return (user, new IssuedTokens(access, accessExpires, refresh, entity.ExpiresAt));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct) =>
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);

    private (string Token, RefreshToken Entity) NewRefresh(Guid userId, string? userAgent)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ua = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent;
        return (token, new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            ExpiresAt = clock.GetUtcNow().AddDays(_jwt.RefreshTokenDays),
            UserAgent = ua,
        });
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
