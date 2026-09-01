using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

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
            status: command.Status,
            externalUrl: command.ExternalUrl,
            alerts: command.Alerts,
            receivedAtUtc: command.ReceivedAtUtc);

        foreach (var item in notifications)
        {
            var notificationResult = Notification.Create(
                key: item.Key,
                topic: item.Topic,
                kind: NotificationKind.MetricAlert,
                message: item.Message,
                createdAtUtc: command.ReceivedAtUtc);

            if (notificationResult.IsFailure)
            {
                return Result.Failure<int>(notificationResult.Errors);
            }

            if (await notificationsRepository.FindByKeyAsync(
                    key: notificationResult.Value.Key,
                    cancellationToken: cancellationToken) is not null)
            {
                continue;
            }

            await notificationsRepository.AddAsync(
                aggregateRoot: notificationResult.Value,
                cancellationToken: cancellationToken);
            queued++;
        }

        return Result.Success(queued);
    }
}
