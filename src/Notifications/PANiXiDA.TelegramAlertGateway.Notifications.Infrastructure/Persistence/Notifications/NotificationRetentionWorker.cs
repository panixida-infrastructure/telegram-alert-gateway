using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.NotificationRetention;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telemetry;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;

public sealed class NotificationRetentionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<NotificationRetentionOptions> options,
    ILogger<NotificationRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(
        options.Value.CleanupIntervalHours);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nextDelay = _cleanupInterval;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cleaner = scope.ServiceProvider.GetRequiredService<NotificationRetentionCleaner>();
                var result = await cleaner.DeleteExpiredAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken);

                GatewayTelemetry.DeletedNotifications.Add(
                    result.SentDeleted,
                    new KeyValuePair<string, object?>("status", "sent"));
                GatewayTelemetry.DeletedNotifications.Add(
                    result.FailedDeleted,
                    new KeyValuePair<string, object?>("status", "failed"));

                logger.LogInformation(
                    "Notification retention cleanup deleted {SentDeleted} sent and {FailedDeleted} failed notifications.",
                    result.SentDeleted,
                    result.FailedDeleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                nextDelay = RetryDelay;
                logger.LogError(exception, "Notification retention cleanup failed.");
            }

            await Task.Delay(
                delay: nextDelay,
                timeProvider: timeProvider,
                cancellationToken: stoppingToken);
        }
    }
}
