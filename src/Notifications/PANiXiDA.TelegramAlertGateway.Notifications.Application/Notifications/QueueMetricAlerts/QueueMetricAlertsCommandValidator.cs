namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;

public sealed class QueueMetricAlertsCommandValidator : AbstractValidator<QueueMetricAlertsCommand>
{
    public QueueMetricAlertsCommandValidator()
    {
        RuleFor(command => command.Status)
            .NotEmpty();

        RuleFor(command => command.Alerts)
            .NotEmpty();
    }
}
