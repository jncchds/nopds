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

    public JwtOptions Jwt { get; set; } = new();
}

public sealed class JwtOptions
{
    /// <summary>HMAC signing key (≥ 32 bytes). Generated and stored in DataDir when empty.</summary>
    public string? Key { get; set; }

    public string Issuer { get; set; } = "nopds";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
