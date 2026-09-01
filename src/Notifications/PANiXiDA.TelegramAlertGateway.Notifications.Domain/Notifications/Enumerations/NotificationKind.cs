namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

public sealed class NotificationKind : Enumeration<NotificationKind>
{
    public const int MaxLength = 50;

    public static readonly NotificationKind MetricAlert = new(
        id: 1,
        name: nameof(MetricAlert));

    public static readonly NotificationKind LogEvent = new(
        id: 2,
        name: nameof(LogEvent));

    private NotificationKind(int id, string name)
        : base(id, name)
    {
    }
}
