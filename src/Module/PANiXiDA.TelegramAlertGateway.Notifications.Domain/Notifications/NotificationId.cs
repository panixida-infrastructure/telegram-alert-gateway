namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

public readonly record struct NotificationId : IStronglyTypedId
{
    private NotificationId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static NotificationId New()
    {
        return new NotificationId(Guid.CreateVersion7());
    }

    public static Result<NotificationId> Create(Guid value)
    {
        return value == Guid.Empty
            ? Result.Failure<NotificationId>(Error.Validation("Notification id cannot be empty."))
            : Result.Success(new NotificationId(value));
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
