using Microsoft.Extensions.DependencyInjection;

namespace Nopds.Telegram;

public static class DependencyInjection
{
    public static IServiceCollection AddNopdsTelegram(this IServiceCollection services) =>
        services.AddHostedService<TelegramBotService>();
}
