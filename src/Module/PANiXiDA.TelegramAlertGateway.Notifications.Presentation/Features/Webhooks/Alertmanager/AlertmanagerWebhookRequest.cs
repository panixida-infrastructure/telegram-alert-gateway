namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Features.Webhooks.Alertmanager;

internal sealed record AlertmanagerWebhookRequest(
    string Status,
    string ExternalUrl,
    IReadOnlyList<AlertmanagerAlertRequest> Alerts);

internal sealed record AlertmanagerAlertRequest(
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string GeneratorUrl,
    string Fingerprint);
