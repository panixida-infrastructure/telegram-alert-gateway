using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

public interface INotificationsRepository : IRepository<NotificationId, Notification>
{
    Task<Notification?> FindByKeyAsync(
        NotificationKey key,
        CancellationToken cancellationToken);
}
