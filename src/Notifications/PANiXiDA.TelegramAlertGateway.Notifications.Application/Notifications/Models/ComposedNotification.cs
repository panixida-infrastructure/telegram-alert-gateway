namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

public sealed record ComposedNotification(
    string Key,
    string Topic,
    string Message);
