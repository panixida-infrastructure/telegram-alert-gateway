using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application.Notifications.QueueLogEvents;

public sealed class QueueLogEventsHandlerTests
{
    [Fact(DisplayName = "Log event handler should add notification when key is unique")]
    public async Task HandleAsync_Should_AddNotification_When_KeyIsUnique()
    {
        var repository = Substitute.For<INotificationsRepository>();
        var composer = Substitute.For<INotificationComposer>();
        var logEvent = new LogEvent(
            Timestamp: DateTimeOffset.UnixEpoch,
            Service: "service",
            Namespace: "namespace",
            Container: "container",
            Owner: "tests",
            Severity: "error",
            Message: "message",
            ExceptionType: null,
            StackTrace: null,
            TraceId: null,
            Fields: new Dictionary<string, string>(),
            Fingerprint: new string('a', 64),
            Occurrences: 1);
        composer.ComposeLogEvent(
                Arg.Any<DateTimeOffset>(),
                logEvent)
            .Returns(new ComposedNotification(
                Key: new string('a', 64),
                Topic: "tests",
                Message: "message"));
        var handler = new QueueLogEventsHandler(repository, composer);
        var command = new QueueLogEventsCommand(
            WindowStartUtc: DateTimeOffset.UnixEpoch,
            Events: [logEvent],
            ReceivedAtUtc: DateTimeOffset.UnixEpoch);

        var result = await handler.HandleAsync(
            command: command,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Value.ShouldBe(1);
        await repository.Received(1).AddAsync(
            aggregateRoot: Arg.Any<Notification>(),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
