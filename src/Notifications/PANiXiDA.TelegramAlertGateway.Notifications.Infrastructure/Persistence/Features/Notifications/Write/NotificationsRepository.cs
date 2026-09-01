using Microsoft.EntityFrameworkCore;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Features.Notifications.Write;

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

    public Task<Notification?> FindByKeyAsync(
        NotificationKey key,
        CancellationToken cancellationToken)
    {
        return _dbContext.Notifications.FirstOrDefaultAsync(
            predicate: item => item.Key == key,
            cancellationToken: cancellationToken);
    }
}
