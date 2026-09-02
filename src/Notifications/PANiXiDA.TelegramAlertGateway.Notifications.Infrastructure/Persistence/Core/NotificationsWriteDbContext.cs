using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

public sealed class NotificationsWriteDbContext(
    DbContextOptions<NotificationsWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<NotificationsWriteDbContext>(options, interceptors)
{
}
