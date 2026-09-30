using Microsoft.AspNetCore.Identity;

namespace Nopds.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public bool IsAdmin { get; set; }

    /// <summary>False for accounts created by single sign-on that still await admin approval.</summary>
    public bool IsApproved { get; set; } = true;

    /// <summary>Secret used in OPDS URLs (/opds/t/{token}/...) for readers without auth support.</summary>
    public string FeedToken { get; set; } = NewToken();

    /// <summary>Libraries the user may see; null means all libraries.</summary>
    public int[]? AllowedLibraryIds { get; set; }

    public bool HideDuplicates { get; set; } = true;

    /// <summary>Preferred UI language (en/uk/pl/de); null means follow the browser.</summary>
    public string? UiLanguage { get; set; }

    /// <summary>Format the web UI downloads by default (converting when possible); null means the book's own format.</summary>
    public string? PreferredFormat { get; set; } = "fb2";

    /// <summary>Telegram user id bound through the bot's deep link; the bot authenticates by it.</summary>
    public long? TelegramUserId { get; set; }

    /// <summary>Telegram username (without @) for display; older links matched by it until the id was known.</summary>
    public string? TelegramUsername { get; set; }

    /// <summary>SHA-256 of the KOReader sync key (KOReader sends md5(password)).</summary>
    public string? KosyncKeyHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool TelegramLinked => TelegramUserId is not null || TelegramUsername is not null;

    /// <summary>Approved and not locked by an admin.</summary>
    public bool CanSignIn(DateTimeOffset now) => IsApproved && !(LockoutEnd is { } end && end > now);

    public static string NewToken() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
}
