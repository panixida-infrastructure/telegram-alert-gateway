using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;

using Telegram.Bot;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telegram;

internal sealed class TelegramClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramOptions> options)
{
    public const string DirectClientName = "telegram-direct";
    public const string ProxiedClientName = "telegram-proxied";

    private readonly TelegramOptions _options = options.Value;

    public ITelegramBotClient CreateDirectClient()
    {
        return Create(DirectClientName);
    }

    public ITelegramBotClient CreateProxiedClient()
    {
        return Create(ProxiedClientName);
    }

    private ITelegramBotClient Create(string httpClientName)
    {
        return new TelegramBotClient(
            new TelegramBotClientOptions(_options.BotToken),
            httpClientFactory.CreateClient(httpClientName));
    }
}
