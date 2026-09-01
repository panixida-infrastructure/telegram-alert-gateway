using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Persistence;

public sealed class NotificationPersistenceTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Save changes async should persist enumeration names when notification is added")]
    public async Task SaveChangesAsync_Should_PersistEnumerationNames_When_NotificationIsAdded()
    {
        var now = new DateTimeOffset(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);
        var notification = Notification.Create(
            key: new string('a', 64),
            topic: "tests",
            kind: NotificationKind.MetricAlert,
            message: "test notification",
            createdAtUtc: now).Value;
        notification.MarkProcessing(now: now);
        await using var scope = Fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        dbContext.Notifications.Add(notification);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var storedKind = await dbContext.Database
            .SqlQueryRaw<string>("SELECT kind AS \"Value\" FROM notifications")
            .SingleAsync(TestContext.Current.CancellationToken);
        var storedStatus = await dbContext.Database
            .SqlQueryRaw<string>("SELECT delivery_status AS \"Value\" FROM notifications")
            .SingleAsync(TestContext.Current.CancellationToken);

        storedKind.ShouldBe(NotificationKind.MetricAlert.Name);
        storedStatus.ShouldBe(NotificationStatus.Processing.Name);
    }
}
