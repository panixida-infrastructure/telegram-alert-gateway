using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

public sealed class NotificationsReadDbContext(
    DbContextOptions<NotificationsReadDbContext> options)
    : ReadDbContext<NotificationsReadDbContext>(options);
