using Microsoft.AspNetCore.Identity;

namespace Nopds.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public bool IsAdmin { get; set; }

    /// <summary>Secret used in OPDS URLs (/opds/t/{token}/...) for readers without auth support.</summary>
    public string FeedToken { get; set; } = NewToken();

    /// <summary>Libraries the user may see; null means all libraries.</summary>
    public int[]? AllowedLibraryIds { get; set; }

    public bool HideDuplicates { get; set; } = true;

    /// <summary>Preferred UI language (en/uk/pl/de); null means follow the browser.</summary>
    public string? UiLanguage { get; set; }

    /// <summary>Telegram username (without @) linked to this account for bot authentication.</summary>
    public string? TelegramUsername { get; set; }

    /// <summary>SHA-256 of the KOReader sync key (KOReader sends md5(password)).</summary>
    public string? KosyncKeyHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string NewToken() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
}
