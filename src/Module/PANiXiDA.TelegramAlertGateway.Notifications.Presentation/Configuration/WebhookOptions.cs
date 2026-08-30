namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Configuration;

internal sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    public string Token { get; init; } = string.Empty;
}
