using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;

public sealed class QueueMetricAlertsHandler(
    INotificationsRepository notificationsRepository,
    INotificationComposer notificationComposer)
    : ICommandHandler<QueueMetricAlertsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        QueueMetricAlertsCommand command,
        CancellationToken cancellationToken)
    {
        var queued = 0;
        var notifications = notificationComposer.ComposeMetricAlerts(
            command.Status,
            command.ExternalUrl,
            command.Alerts,
            command.ReceivedAtUtc);

        foreach (var item in notifications)
        {
            if (await notificationsRepository.ExistsByKeyAsync(item.Key, cancellationToken))
            {
                continue;
            }

            var notificationResult = Notification.Create(
                item.Key,
                item.Topic,
                NotificationKind.MetricAlert,
                item.Message,
                command.ReceivedAtUtc);

            if (notificationResult.IsFailure)
            {
                return Result.Failure<int>(notificationResult.Errors);
            }

            await notificationsRepository.AddAsync(notificationResult.Value, cancellationToken);
            queued++;
        }

        return Result.Success(queued);
    }
}
