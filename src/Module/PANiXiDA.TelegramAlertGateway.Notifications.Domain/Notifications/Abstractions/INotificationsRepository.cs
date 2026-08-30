namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

public interface INotificationsRepository : IRepository<NotificationId, Notification>
{
    Task<bool> ExistsByKeyAsync(string key, CancellationToken cancellationToken);
}
