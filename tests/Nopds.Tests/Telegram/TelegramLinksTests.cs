using Nopds.Telegram;

namespace Nopds.Tests.Telegram;

public class TelegramLinksTests
{
    [Fact]
    public void Token_links_its_user_once_and_reports_completion_to_that_user_only()
    {
        var links = new TelegramLinks();
        var user = Guid.NewGuid();
        var token = links.Create(user);

        Assert.Matches("^[0-9a-f]{32}$", token); // fits Telegram's start parameter alphabet and 64-char limit
        Assert.False(links.IsUsed(token, user));
        Assert.Equal(user, links.Consume(token));
        Assert.Null(links.Consume(token));
        Assert.True(links.IsUsed(token, user));
        Assert.False(links.IsUsed(token, Guid.NewGuid()));
    }

    [Fact]
    public void Unknown_token_links_nobody() => Assert.Null(new TelegramLinks().Consume("0123456789abcdef0123456789abcdef"));
}
