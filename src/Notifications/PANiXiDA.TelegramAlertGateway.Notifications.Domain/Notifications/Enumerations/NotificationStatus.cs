namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

public sealed class NotificationStatus : Enumeration<NotificationStatus>
{
    public const int MaxLength = 50;

    public static readonly NotificationStatus Pending = new(
        id: 1,
        name: nameof(Pending));

    public static readonly NotificationStatus Processing = new(
        id: 2,
        name: nameof(Processing));

    public static readonly NotificationStatus Sent = new(
        id: 3,
        name: nameof(Sent));

    public static readonly NotificationStatus Failed = new(
        id: 4,
        name: nameof(Failed));

    private NotificationStatus(int id, string name)
        : base(id, name)
    {
    }
}
