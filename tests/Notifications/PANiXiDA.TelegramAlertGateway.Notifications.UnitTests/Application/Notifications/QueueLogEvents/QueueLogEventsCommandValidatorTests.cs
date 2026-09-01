using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application.Notifications.QueueLogEvents;

public sealed class QueueLogEventsCommandValidatorTests
{
    [Fact(DisplayName = "Queue log events validator should return error when events are empty")]
    public void Validate_Should_ReturnError_When_EventsAreEmpty()
    {
        var validator = new QueueLogEventsCommandValidator();
        var command = new QueueLogEventsCommand(
            WindowStartUtc: DateTimeOffset.UnixEpoch,
            Events: Array.Empty<LogEvent>(),
            ReceivedAtUtc: DateTimeOffset.UnixEpoch);

        var result = validator.Validate(command);

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(QueueLogEventsCommand.Events));
    }
}
