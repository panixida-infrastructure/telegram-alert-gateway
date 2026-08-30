using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;

public sealed class QueueLogEventsHandler(
    INotificationsRepository notificationsRepository,
    INotificationComposer notificationComposer)
    : ICommandHandler<QueueLogEventsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        QueueLogEventsCommand command,
        CancellationToken cancellationToken)
    {
        var queued = 0;

        foreach (var logEvent in command.Events)
        {
            var item = notificationComposer.ComposeLogEvent(command.WindowStartUtc, logEvent);
            if (await notificationsRepository.ExistsByKeyAsync(item.Key, cancellationToken))
            {
                continue;
            }

            var notificationResult = Notification.Create(
                item.Key,
                item.Topic,
                NotificationKind.LogEvent,
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
