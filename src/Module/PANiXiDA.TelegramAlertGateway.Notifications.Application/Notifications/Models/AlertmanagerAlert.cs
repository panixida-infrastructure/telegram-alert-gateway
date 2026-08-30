namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

public sealed record AlertmanagerAlert(
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string GeneratorUrl,
    string Fingerprint);
