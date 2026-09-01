using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application.Notifications.QueueMetricAlerts;

public sealed class QueueMetricAlertsCommandValidatorTests
{
    [Fact(DisplayName = "Queue metric alerts validator should return errors when required values are missing")]
    public void Validate_Should_ReturnErrors_When_RequiredValuesAreMissing()
    {
        var validator = new QueueMetricAlertsCommandValidator();
        var command = new QueueMetricAlertsCommand(
            Status: string.Empty,
            ExternalUrl: string.Empty,
            Alerts: [],
            ReceivedAtUtc: DateTimeOffset.UnixEpoch);

        var result = validator.Validate(command);

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(QueueMetricAlertsCommand.Status));
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(QueueMetricAlertsCommand.Alerts));
    }
}
