using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

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
            var notificationResult = Notification.Create(
                key: item.Key,
                topic: item.Topic,
                kind: NotificationKind.LogEvent,
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
