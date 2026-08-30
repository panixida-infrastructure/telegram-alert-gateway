using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application.Notifications;

public sealed class QueueMetricAlertsHandlerTests
{
    [Fact(DisplayName = "Metric alert handler ignores an existing notification key")]
    public async Task HandleAsync_Should_Not_Add_Notification_When_Key_Already_Exists()
    {
        var repository = Substitute.For<INotificationsRepository>();
        var composer = Substitute.For<INotificationComposer>();
        var item = new ComposedNotification(
            new string('a', 64),
            "tactical-heroes",
            "message");
        composer.ComposeMetricAlerts(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<AlertmanagerAlert>>())
            .Returns([item]);
        repository.ExistsByKeyAsync(item.Key, Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new QueueMetricAlertsHandler(repository, composer);

        var result = await handler.HandleAsync(
            new QueueMetricAlertsCommand("firing", string.Empty, [], DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        result.Value.ShouldBe(0);
        await repository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, TestContext.Current.CancellationToken);
    }
}
