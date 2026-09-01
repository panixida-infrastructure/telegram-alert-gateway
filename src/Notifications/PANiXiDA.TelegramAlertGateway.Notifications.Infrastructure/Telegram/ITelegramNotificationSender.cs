namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telegram;

internal interface ITelegramNotificationSender
{
    Task SendAsync(string topic, string message, CancellationToken cancellationToken);
}
