using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.NotificationRetention;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Persistence;

public sealed class NotificationRetentionCleanerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Delete expired async should delete only expired terminal notifications in batches when retention runs")]
    public async Task DeleteExpiredAsync_Should_DeleteOnlyExpiredTerminalNotificationsInBatches_When_RetentionRuns()
    {
        var now = new DateTimeOffset(2026, 8, 30, 18, 0, 0, TimeSpan.Zero);
        await using var scope = Fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var notifications = new[]
        {
            CreateSent("expired-sent-1", now.AddDays(-16), now.AddDays(-15)),
            CreateSent("expired-sent-2", now.AddDays(-16), now.AddDays(-15)),
            CreateSent("expired-sent-3", now.AddDays(-16), now.AddDays(-15)),
            CreateSent("recent-sent", now.AddDays(-14), now.AddDays(-13)),
            CreateFailed("expired-failed", now.AddDays(-32), now.AddDays(-31)),
            CreateFailed("recent-failed", now.AddDays(-30), now.AddDays(-29)),
            CreatePending("old-pending", now.AddDays(-60)),
            CreateProcessing("old-processing", now.AddDays(-60))
        };
        dbContext.Set<Notification>().AddRange(notifications);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cleaner = scope.ServiceProvider.GetRequiredService<NotificationRetentionCleaner>();

        var result = await cleaner.DeleteExpiredAsync(
            now,
            TestContext.Current.CancellationToken);

        result.SentDeleted.ShouldBe(3);
        result.FailedDeleted.ShouldBe(1);
        var remainingKeys = (await dbContext.Set<Notification>()
            .AsNoTracking()
            .Select(item => item.Key)
            .ToArrayAsync(TestContext.Current.CancellationToken))
            .Select(item => item.Value)
            .OrderBy(item => item)
            .ToArray();
        string[] expectedKeys =
        [
            CreateKey("old-pending"),
            CreateKey("old-processing"),
            CreateKey("recent-failed"),
            CreateKey("recent-sent")
        ];
        remainingKeys.ShouldBe(expectedKeys.OrderBy(item => item));
    }

    [Fact(DisplayName = "Start async should run cleanup immediately when worker starts")]
    public async Task StartAsync_Should_RunCleanupImmediately_When_WorkerStarts()
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = Fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        dbContext.Set<Notification>().Add(CreateSent(
            "worker-expired-sent",
            now.AddDays(-16),
            now.AddDays(-15)));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var worker = new NotificationRetentionWorker(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<IOptions<NotificationRetentionOptions>>(),
            NullLogger<NotificationRetentionWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var deleted = false;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                dbContext.ChangeTracker.Clear();
                deleted = !await dbContext.Set<Notification>()
                    .AsNoTracking()
                    .AnyAsync(TestContext.Current.CancellationToken);
                if (deleted)
                {
                    break;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    TestContext.Current.CancellationToken);
            }

            deleted.ShouldBeTrue();
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static Notification CreatePending(string key, DateTimeOffset createdAtUtc)
    {
        return CreateNotification(key, createdAtUtc);
    }

    private static Notification CreateProcessing(string key, DateTimeOffset createdAtUtc)
    {
        var notification = CreateNotification(key, createdAtUtc);
        notification.MarkProcessing(createdAtUtc.AddMinutes(1));

        return notification;
    }

    private static Notification CreateSent(
        string key,
        DateTimeOffset createdAtUtc,
        DateTimeOffset sentAtUtc)
    {
        var notification = CreateNotification(key, createdAtUtc);
        notification.MarkProcessing(createdAtUtc.AddMinutes(1));
        notification.MarkSent(sentAtUtc);

        return notification;
    }

    private static Notification CreateFailed(
        string key,
        DateTimeOffset createdAtUtc,
        DateTimeOffset failedAtUtc)
    {
        var notification = CreateNotification(key, createdAtUtc);
        notification.MarkProcessing(createdAtUtc.AddMinutes(1));
        notification.MarkFailed(failedAtUtc, "delivery failed");

        return notification;
    }

    private static Notification CreateNotification(string key, DateTimeOffset createdAtUtc)
    {
        return Notification.Create(
            CreateKey(key),
            "tests",
            NotificationKind.LogEvent,
            key,
            createdAtUtc).Value;
    }

    private static string CreateKey(string value)
    {
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
