using Microsoft.EntityFrameworkCore;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;

public sealed class NotificationsRepository
    : EfRepository<NotificationsWriteDbContext, NotificationId, Notification>,
        INotificationsRepository
{
    private readonly NotificationsWriteDbContext _dbContext;

    public NotificationsRepository(
        NotificationsWriteDbContext dbContext,
        IAggregateTracker aggregateTracker)
        : base(dbContext, aggregateTracker)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsByKeyAsync(string key, CancellationToken cancellationToken)
    {
        var notificationKey = NotificationKey.Create(key).Value;

        return _dbContext.Notifications.AnyAsync(
            item => item.Key == notificationKey,
            cancellationToken);
    }
}
