using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;

public sealed record QueueLogEventsCommand(
    DateTimeOffset WindowStartUtc,
    IReadOnlyList<LogEvent> Events,
    DateTimeOffset ReceivedAtUtc) : ICommand<Result<int>>;
