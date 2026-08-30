using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;

public sealed record QueueMetricAlertsCommand(
    string Status,
    string ExternalUrl,
    IReadOnlyList<AlertmanagerAlert> Alerts,
    DateTimeOffset ReceivedAtUtc) : ICommand<Result<int>>;
