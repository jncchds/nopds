using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Nopds.Telegram;

/// <summary>
/// One-time deep links (t.me/{bot}?start={token}) that bind a Telegram account to a web user.
/// The bot stores the numeric Telegram user id, so accounts without a public username work too.
/// </summary>
public sealed class TelegramLinks
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly MemoryCache _tokens = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    /// <summary>User name of the running bot; null while the bot is disabled or not connected.</summary>
    public string? BotUsername { get; internal set; }

    public string Create(Guid userId)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _tokens.Set(token, userId, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return token;
    }

    internal Guid? Consume(string token)
    {
        if (!_tokens.TryGetValue(token, out Guid userId))
        {
            return null;
        }

        _tokens.Remove(token);
        _tokens.Set(Used(token), userId, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return userId;
    }

    /// <summary>True once the bot has consumed the user's token, so the page can stop waiting.</summary>
    public bool IsUsed(string token, Guid userId) => _tokens.TryGetValue(Used(token), out Guid owner) && owner == userId;

    private static string Used(string token) => "used:" + token;
}
