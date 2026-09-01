using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;

public interface INotificationComposer
{
    IReadOnlyList<ComposedNotification> ComposeMetricAlerts(
        string status,
        string externalUrl,
        IReadOnlyList<AlertmanagerAlert> alerts,
        DateTimeOffset receivedAtUtc);

    ComposedNotification ComposeLogEvent(
        DateTimeOffset windowStartUtc,
        LogEvent logEvent);
}
