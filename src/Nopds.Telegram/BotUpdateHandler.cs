using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nopds.Conversion;
using Nopds.Domain.Text;
using Nopds.Formats;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Identity;
using Nopds.Infrastructure.Settings;
using Nopds.Infrastructure.Uploads;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using L = Nopds.Infrastructure.Localization.ServerStrings;

namespace Nopds.Telegram;

/// <summary>
/// Bot conversation (ported from SimpleOPDS sopds_telebot): free-text search with a choice of books,
/// authors or series, paged inline keyboards, book cards and file delivery (original or converted).
/// Callback data stays under Telegram's 64-byte limit by keeping search terms in a short-lived cache.
/// </summary>
internal sealed class BotUpdateHandler(IServiceScopeFactory scopes, SettingsStore settings, TelegramLinks links, ILogger<BotUpdateHandler> log) : IUpdateHandler
{
    private const long MaxTelegramFile = 50L * 1024 * 1024;
    private static readonly MemoryCache Queries = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    private sealed record Caller(AppUser? User, Scope Scope, string Lang);

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { Text: { } text } message && message.From is { } from)
            {
                await OnMessageAsync(bot, message.Chat.Id, from, text.Trim(), ct);
            }
            else if (update.CallbackQuery is { Data: { } data, Message: { } cbMessage } cb)
            {
                await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                await OnCallbackAsync(bot, cbMessage.Chat.Id, cb.From, data, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Telegram update failed");
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        log.LogWarning(exception, "Telegram polling error ({Source})", source);
        return Task.CompletedTask;
    }

    private async Task<Caller?> IdentifyAsync(IServiceProvider sp, User from, CancellationToken ct)
    {
        var db = sp.GetRequiredService<NopdsDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == from.Id, ct);
        if (user is null && !string.IsNullOrEmpty(from.Username))
        {
            // Links made by typing a username before deep links existed: adopt the id on first contact.
            var name = from.Username.ToLowerInvariant();
            user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == null && u.TelegramUsername != null && u.TelegramUsername.ToLower() == name, ct);
            if (user is not null)
            {
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TelegramUserId, from.Id), ct);
            }
        }
        else if (user is not null && user.TelegramUsername != from.Username)
        {
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.TelegramUsername, from.Username), ct);
        }

        var s = settings.Current;
        if (user is null && s.Telegram.RequireLinkedUser)
        {
            return null;
        }

        if (user is not null && !user.CanSignIn(DateTimeOffset.UtcNow))
        {
            return null;
        }

        var lang = UiLanguages.Match(user?.UiLanguage) ?? UiLanguages.Match(from.LanguageCode) ?? UiLanguages.Default;
        var isAdmin = user?.IsAdmin == true;
        var allowed = isAdmin ? null : sp.GetRequiredService<UploadLibrary>().Extend(user?.AllowedLibraryIds);
        var scope = new Scope(allowed, null, user?.HideDuplicates ?? s.HideDuplicates, s.PreferredFormats, lang, user?.Id, isAdmin);
        return new Caller(user, scope, lang);
    }

    private async Task OnMessageAsync(ITelegramBotClient bot, long chat, User from, string text, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (command, rest) = SplitCommand(text);
        if (command == "/start" && rest.Length > 0)
        {
            await LinkAsync(bot, chat, scope.ServiceProvider, from, rest, ct);
            return;
        }

        var caller = await IdentifyAsync(scope.ServiceProvider, from, ct);
        var lang = caller?.Lang ?? UiLanguages.Match(from.LanguageCode) ?? UiLanguages.Default;
        if (caller is null)
        {
            await bot.SendMessage(chat, L.Get(lang, "tg.denied"), cancellationToken: ct);
            return;
        }

        switch (command)
        {
            case "/start":
            case "/help":
                await bot.SendMessage(chat, L.Get(lang, "tg.welcome"), cancellationToken: ct);
                return;
            case "/books":
            case "/authors":
            case "/series":
                if (rest.Length > 0)
                {
                    await SearchAsync(bot, chat, scope.ServiceProvider, caller, command[1], Remember(rest), 1, ct);
                }

                return;
        }

        if (text.StartsWith('/') || text.Length == 0)
        {
            await bot.SendMessage(chat, L.Get(lang, "tg.welcome"), cancellationToken: ct);
            return;
        }

        var qid = Remember(text);
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData(L.Get(lang, "tg.books"), $"q:b:{qid}:1"),
                InlineKeyboardButton.WithCallbackData(L.Get(lang, "tg.authors"), $"q:a:{qid}:1"),
                InlineKeyboardButton.WithCallbackData(L.Get(lang, "tg.series"), $"q:s:{qid}:1"),
            ],
        ]);
        await bot.SendMessage(chat, L.Format(lang, "tg.choose", text), replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task LinkAsync(ITelegramBotClient bot, long chat, IServiceProvider sp, User from, string token, CancellationToken ct)
    {
        var db = sp.GetRequiredService<NopdsDbContext>();
        var userId = links.Consume(token);
        var user = userId is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var lang = UiLanguages.Match(user?.UiLanguage) ?? UiLanguages.Match(from.LanguageCode) ?? UiLanguages.Default;
        if (user is null)
        {
            await bot.SendMessage(chat, L.Get(lang, "tg.linkExpired"), cancellationToken: ct);
            return;
        }

        // One Telegram account belongs to one web user: move it if it was linked elsewhere.
        await db.Users.Where(u => u.TelegramUserId == from.Id && u.Id != user.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.TelegramUserId, (long?)null).SetProperty(u => u.TelegramUsername, (string?)null), ct);
        user.TelegramUserId = from.Id;
        user.TelegramUsername = from.Username;
        await db.SaveChangesAsync(ct);
        await bot.SendMessage(chat, L.Format(lang, "tg.linked", user.UserName ?? string.Empty) + "\n\n" + L.Get(lang, "tg.welcome"), cancellationToken: ct);
    }

    private async Task OnCallbackAsync(ITelegramBotClient bot, long chat, User from, string data, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var caller = await IdentifyAsync(sp, from, ct);
        if (caller is null)
        {
            await bot.SendMessage(chat, L.Get(UiLanguages.Match(from.LanguageCode) ?? "en", "tg.denied"), cancellationToken: ct);
            return;
        }

        var p = data.Split(':');
        switch (p[0])
        {
            case "q" when p.Length == 4:
                await SearchAsync(bot, chat, sp, caller, p[1][0], p[2], int.Parse(p[3]), ct);
                break;
            case "a" when p.Length == 3:
                await BooksAsync(bot, chat, sp, caller, new BookQuery { AuthorId = long.Parse(p[1]) }, $"a:{p[1]}", int.Parse(p[2]), ct);
                break;
            case "s" when p.Length == 3:
                await BooksAsync(bot, chat, sp, caller, new BookQuery { SeriesId = long.Parse(p[1]), Sort = BookSort.SeriesNumber }, $"s:{p[1]}", int.Parse(p[2]), ct);
                break;
            case "b" when p.Length == 2:
                await BookCardAsync(bot, chat, sp, caller, long.Parse(p[1]), ct);
                break;
            case "d" when p.Length == 3:
                await SendBookAsync(bot, chat, sp, caller, long.Parse(p[1]), p[2], ct);
                break;
        }
    }

    private async Task SearchAsync(ITelegramBotClient bot, long chat, IServiceProvider sp, Caller caller, char kind, string qid, int page, CancellationToken ct)
    {
        if (!Queries.TryGetValue(qid, out string? text) || text is null)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "tg.welcome"), cancellationToken: ct);
            return;
        }

        var catalog = sp.GetRequiredService<CatalogService>();
        var size = settings.Current.Telegram.MaxItems;
        if (kind == 'b')
        {
            await BooksAsync(bot, chat, sp, caller, new BookQuery { Text = text, Match = TextMatch.Contains }, $"q:b:{qid}", page, ct);
            return;
        }

        var query = new NameQuery { Text = text, Match = TextMatch.Contains, Page = page, PageSize = size };
        var result = kind == 'a' ? await catalog.AuthorsAsync(caller.Scope, query, ct) : await catalog.SeriesAsync(caller.Scope, query, ct);
        if (result.Items.Count == 0)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "nothing"), cancellationToken: ct);
            return;
        }

        var rows = result.Items
            .Select(i => new[] { InlineKeyboardButton.WithCallbackData($"{Trim(i.Name, 50)} ({i.Books})", $"{(kind == 'a' ? 'a' : 's')}:{i.Id}:1") })
            .ToList();
        if (result.HasNext)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData(L.Get(caller.Lang, "tg.more"), $"q:{kind}:{qid}:{page + 1}")]);
        }

        var title = L.Get(caller.Lang, kind == 'a' ? "found.authors" : "found.series");
        await bot.SendMessage(chat, $"{title}: {text}", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task BooksAsync(ITelegramBotClient bot, long chat, IServiceProvider sp, Caller caller, BookQuery query, string pagePrefix, int page, CancellationToken ct)
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var size = settings.Current.Telegram.MaxItems;
        var result = await catalog.BooksAsync(caller.Scope, query with { Page = page, PageSize = size }, ct);
        if (result.Items.Count == 0)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "nothing"), cancellationToken: ct);
            return;
        }

        var rows = result.Items.Select(b =>
        {
            var author = b.Authors.FirstOrDefault()?.Name;
            var label = author is null ? b.Title : $"{b.Title} — {author}";
            return new[] { InlineKeyboardButton.WithCallbackData($"{Trim(label, 60)} [{b.Format}]", $"b:{b.Id}") };
        }).ToList();
        if (result.HasNext)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData(L.Get(caller.Lang, "tg.more"), $"{pagePrefix}:{page + 1}")]);
        }

        await bot.SendMessage(chat, L.Get(caller.Lang, "found.books"), replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task BookCardAsync(ITelegramBotClient bot, long chat, IServiceProvider sp, Caller caller, long id, CancellationToken ct)
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var conversion = sp.GetRequiredService<ConversionService>();
        var b = await catalog.BookAsync(caller.Scope, id, ct);
        if (b is null)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "nothing"), cancellationToken: ct);
            return;
        }

        var html = new StringBuilder();
        html.Append("<b>").Append(WebUtility.HtmlEncode(b.Title)).Append("</b>\n");
        if (b.Authors.Count > 0)
        {
            html.Append(WebUtility.HtmlEncode(string.Join(", ", b.Authors.Select(a => a.Name)))).Append('\n');
        }

        foreach (var s in b.Series)
        {
            html.Append("<i>").Append(WebUtility.HtmlEncode(s.Number > 0 ? $"{s.Name} #{s.Number}" : s.Name)).Append("</i>\n");
        }

        if (b.Genres.Count > 0)
        {
            html.Append(WebUtility.HtmlEncode(string.Join(", ", b.Genres.Select(g => g.Name)))).Append('\n');
        }

        html.Append($"{b.Format.ToUpperInvariant()} · {Math.Max(1, b.FileSize / 1024)} KB");
        if (!string.IsNullOrEmpty(b.Annotation))
        {
            html.Append("\n\n").Append(WebUtility.HtmlEncode(Trim(b.Annotation, 700)));
        }

        var buttons = new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"⬇ {b.Format.ToUpperInvariant()}", $"d:{b.Id}:-") };
        buttons.AddRange(conversion.TargetsFor(b.Format).Select(f => InlineKeyboardButton.WithCallbackData($"⬇ {f.ToUpperInvariant()}", $"d:{b.Id}:{f}")));
        var rows = new List<InlineKeyboardButton[]> { buttons.ToArray() };
        foreach (var a in b.Authors.Take(2))
        {
            rows.Add([InlineKeyboardButton.WithCallbackData($"👤 {Trim(a.Name, 40)}", $"a:{a.Id}:1")]);
        }

        foreach (var s in b.Series.Take(2))
        {
            rows.Add([InlineKeyboardButton.WithCallbackData($"📚 {Trim(s.Name, 40)}", $"s:{s.Id}:1")]);
        }

        await bot.SendMessage(chat, html.ToString(), parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task SendBookAsync(ITelegramBotClient bot, long chat, IServiceProvider sp, Caller caller, long id, string format, CancellationToken ct)
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var book = await catalog.BookEntityAsync(caller.Scope, id, ct);
        if (book?.Library is null)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "nothing"), cancellationToken: ct);
            return;
        }

        var target = format == "-" ? book.Format : format;
        Stream? stream;
        if (target == book.Format)
        {
            stream = BookStorage.Open(book.Library, book);
        }
        else
        {
            var converted = await sp.GetRequiredService<ConversionService>().ConvertAsync(book.Library, book, target, ct);
            stream = converted is null ? null : File.OpenRead(converted.Path);
        }

        if (stream is null)
        {
            await bot.SendMessage(chat, L.Get(caller.Lang, "tg.error"), cancellationToken: ct);
            return;
        }

        await using (stream)
        {
            if (stream.CanSeek && stream.Length > MaxTelegramFile)
            {
                await bot.SendMessage(chat, L.Get(caller.Lang, "tg.toolarge"), cancellationToken: ct);
                return;
            }

            var name = Translit.ToAscii(Trim(book.Title, 80)) + "." + target;
            await bot.SendDocument(chat, InputFile.FromStream(stream, name), caption: book.Title, cancellationToken: ct);
        }

        if (caller.User is { } user)
        {
            await MarkShelfAsync(sp, user.Id, id, ct);
        }
    }

    private static async Task MarkShelfAsync(IServiceProvider sp, Guid userId, long bookId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<NopdsDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (await db.ReadingStates.Where(r => r.UserId == userId && r.BookId == bookId).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastOpenedAt, now), ct) == 0)
        {
            db.ReadingStates.Add(new Domain.Entities.ReadingState { UserId = userId, BookId = bookId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
            }
        }
    }

    private static (string Command, string Args) SplitCommand(string text)
    {
        if (!text.StartsWith('/'))
        {
            return (string.Empty, text);
        }

        var space = text.IndexOf(' ');
        var cmd = (space < 0 ? text : text[..space]).Split('@')[0].ToLowerInvariant();
        return (cmd, space < 0 ? string.Empty : text[(space + 1)..].Trim());
    }

    private static string Remember(string text)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        Queries.Set(id, text, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(6) });
        return id;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
