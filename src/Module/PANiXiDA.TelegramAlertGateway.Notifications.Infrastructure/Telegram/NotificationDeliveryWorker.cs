using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telegram;

internal sealed class NotificationDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification delivery loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var now = timeProvider.GetUtcNow();
        var staleProcessingThreshold = now.AddMinutes(-5);
        var notification = await dbContext.Notifications
            .Where(item =>
                (item.Status == NotificationStatus.Pending && item.AvailableAtUtc <= now)
                || (item.Status == NotificationStatus.Processing
                    && item.AvailableAtUtc <= staleProcessingThreshold))
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (notification is null)
        {
            return false;
        }

        if (notification.Status == NotificationStatus.Processing)
        {
            notification.Reschedule(now, "Recovered stale processing delivery.");
        }

        notification.MarkProcessing(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        var sender = scope.ServiceProvider.GetRequiredService<ITelegramNotificationSender>();

        try
        {
            await sender.SendAsync(
                notification.Topic.Value,
                notification.Message,
                cancellationToken);
            notification.MarkSent(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Notification {NotificationId} was sent to topic {Topic}.",
                notification.Id,
                notification.Topic.Value);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var failureTime = timeProvider.GetUtcNow();
            if (notification.Attempts >= MaxAttempts)
            {
                notification.MarkFailed(failureTime, exception.Message);
            }
            else
            {
                var delaySeconds = Math.Min(
                    Math.Pow(2, notification.Attempts),
                    TimeSpan.FromMinutes(5).TotalSeconds);
                notification.Reschedule(
                    failureTime.AddSeconds(delaySeconds),
                    exception.Message);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                exception,
                "Notification {NotificationId} delivery attempt {Attempt} failed.",
                notification.Id,
                notification.Attempts);
        }

        return true;
    }
}
