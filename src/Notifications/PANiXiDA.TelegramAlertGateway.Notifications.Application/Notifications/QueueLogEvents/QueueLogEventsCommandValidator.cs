namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;

public sealed class QueueLogEventsCommandValidator : AbstractValidator<QueueLogEventsCommand>
{
    public QueueLogEventsCommandValidator()
    {
        RuleFor(command => command.Events)
            .NotEmpty();
    }
}
