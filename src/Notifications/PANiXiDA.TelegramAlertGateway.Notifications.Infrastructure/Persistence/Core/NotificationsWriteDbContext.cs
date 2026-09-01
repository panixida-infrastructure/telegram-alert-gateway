using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

public sealed class NotificationsWriteDbContext(
    DbContextOptions<NotificationsWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<NotificationsWriteDbContext>(options, interceptors)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LogIngestionCheckpoint> LogIngestionCheckpoints => Set<LogIngestionCheckpoint>();
}
