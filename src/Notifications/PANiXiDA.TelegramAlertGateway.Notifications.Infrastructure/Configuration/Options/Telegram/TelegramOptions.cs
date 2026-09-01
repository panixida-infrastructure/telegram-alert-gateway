namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; init; } = string.Empty;
    public long ChatId { get; init; }
    public string ProxyUrl { get; init; } = string.Empty;
    public Dictionary<string, int> Topics { get; init; } = new(StringComparer.Ordinal);
}
