namespace Nopds.Web.Infrastructure;

/// <summary>Static host configuration (appsettings / environment, section "Nopds").</summary>
public sealed class NopdsOptions
{
    public const string Section = "Nopds";

    /// <summary>Directory for persistent app data (signing key, logs).</summary>
    public string DataDir { get; set; } = "data";

    /// <summary>Directory for covers, thumbnails and conversion cache.</summary>
    public string CacheDir { get; set; } = "data/cache";

    public bool AutoMigrate { get; set; } = true;

    /// <summary>Initial admin account created on first start when no users exist.</summary>
    public string? AdminUser { get; set; }

    public string? AdminPassword { get; set; }

    /// <summary>
    /// On every start, (re)creates <see cref="AdminUser"/> with <see cref="AdminPassword"/> and restores its admin
    /// rights, approval and unlocks it. A recovery switch for a lost password or a demoted main admin.
    /// </summary>
    public bool AdminForce { get; set; }

    public JwtOptions Jwt { get; set; } = new();

    public OidcOptions Oidc { get; set; } = new();
}

public sealed class JwtOptions
{
    /// <summary>HMAC signing key (≥ 32 bytes). Generated and stored in DataDir when empty.</summary>
    public string? Key { get; set; }

    public string Issuer { get; set; } = "nopds";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Single sign-on through an OpenID Connect provider such as Authentik.</summary>
public sealed class OidcOptions
{
    /// <summary>Issuer URL, e.g. https://auth.example.com/application/o/nopds/ for Authentik. SSO is off when empty.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Provider name shown on the login button.</summary>
    public string DisplayName { get; set; } = "Authentik";

    public string[] Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>Allows an http:// authority (development only).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}
