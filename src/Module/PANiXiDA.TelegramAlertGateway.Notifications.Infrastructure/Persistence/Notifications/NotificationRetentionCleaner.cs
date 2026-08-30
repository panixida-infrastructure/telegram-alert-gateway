using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;

public sealed class NotificationRetentionCleaner(
    NotificationsWriteDbContext dbContext,
    IOptions<NotificationRetentionOptions> options)
{
    private readonly NotificationRetentionOptions _options = options.Value;

    public async Task<NotificationRetentionResult> DeleteExpiredAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var sentDeleted = await DeleteSentAsync(
            nowUtc.AddDays(-_options.SentRetentionDays),
            cancellationToken);
        var failedDeleted = await DeleteFailedAsync(
            nowUtc.AddDays(-_options.FailedRetentionDays),
            cancellationToken);

        return new NotificationRetentionResult(sentDeleted, failedDeleted);
    }

    private async Task<int> DeleteSentAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deleted;

        do
        {
            deleted = await dbContext.Notifications
                .Where(item =>
                    item.Status == NotificationStatus.Sent
                    && item.SentAtUtc < cutoffUtc)
                .OrderBy(item => item.SentAtUtc)
                .Take(_options.BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            totalDeleted += deleted;
        }
        while (deleted == _options.BatchSize);

        return totalDeleted;
    }

    private async Task<int> DeleteFailedAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0;
        int deleted;

        do
        {
            deleted = await dbContext.Notifications
                .Where(item =>
                    item.Status == NotificationStatus.Failed
                    && item.AvailableAtUtc < cutoffUtc)
                .OrderBy(item => item.AvailableAtUtc)
                .Take(_options.BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            totalDeleted += deleted;
        }
        while (deleted == _options.BatchSize);

        return totalDeleted;
    }
}

public sealed record NotificationRetentionResult(
    int SentDeleted,
    int FailedDeleted)
{
    public int TotalDeleted => SentDeleted + FailedDeleted;
}
