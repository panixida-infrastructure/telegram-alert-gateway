using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application.Notifications.QueueMetricAlerts;

public sealed class QueueMetricAlertsHandlerTests
{
    [Fact(DisplayName = "Metric alert handler should ignore an existing notification when key already exists")]
    public async Task HandleAsync_Should_IgnoreNotification_When_KeyAlreadyExists()
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
                Arg.Any<IReadOnlyList<AlertmanagerAlert>>(),
                Arg.Any<DateTimeOffset>())
            .Returns([item]);
        repository.FindByKeyAsync(
                Arg.Is<NotificationKey>(key => key.Value == item.Key),
                Arg.Any<CancellationToken>())
            .Returns(Notification.Create(
                key: item.Key,
                topic: item.Topic,
                kind: NotificationKind.MetricAlert,
                message: item.Message,
                createdAtUtc: DateTimeOffset.UnixEpoch).Value);
        var handler = new QueueMetricAlertsHandler(repository, composer);

        var result = await handler.HandleAsync(
            new QueueMetricAlertsCommand("firing", string.Empty, [], DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        result.Value.ShouldBe(0);
        await repository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, TestContext.Current.CancellationToken);
    }
}
