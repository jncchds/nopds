using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nopds.Infrastructure.Settings;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Nopds.Telegram;

/// <summary>
/// Optional Telegram bot hosted inside the web app. It runs only when enabled with a token in the
/// admin settings and restarts automatically when those settings change (long polling, no webhook).
/// </summary>
public sealed class TelegramBotService(SettingsStore settings, IServiceScopeFactory scopes, ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger<TelegramBotService>();
    private CancellationTokenSource? _restart;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += OnSettingsChanged;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _restart = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var tg = settings.Current.Telegram;
                if (!tg.Enabled || string.IsNullOrWhiteSpace(tg.BotToken))
                {
                    await WaitAsync(_restart.Token);
                    continue;
                }

                try
                {
                    var bot = new TelegramBotClient(tg.BotToken);
                    var me = await bot.GetMe(_restart.Token);
                    _log.LogInformation("Telegram bot @{Bot} started", me.Username);
                    var handler = new BotUpdateHandler(scopes, settings, loggers.CreateLogger<BotUpdateHandler>());
                    await bot.ReceiveAsync(handler, new ReceiverOptions
                    {
                        AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
                        DropPendingUpdates = true,
                    }, _restart.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogInformation("Telegram bot restarting after settings change");
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "Telegram bot stopped; retrying in 60 s");
                    await WaitAsync(_restart.Token, TimeSpan.FromSeconds(60));
                }
            }
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(AppSettings _) => _restart?.Cancel();

    private static async Task WaitAsync(CancellationToken ct, TimeSpan? delay = null)
    {
        try
        {
            await Task.Delay(delay ?? Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
