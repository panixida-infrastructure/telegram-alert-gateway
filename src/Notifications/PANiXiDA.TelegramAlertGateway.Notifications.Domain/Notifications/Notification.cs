using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

public sealed class Notification : AggregateRoot<NotificationId>
{
    private Notification(
        NotificationId id,
        NotificationKey key,
        TopicName topic,
        NotificationKind kind,
        NotificationMessage message)
        : base(id)
    {
        Key = key;
        Topic = topic;
        Kind = kind;
        Message = message;
        Delivery = NotificationDelivery.Start(
            createdAtUtc: DateTimeOffset.UnixEpoch);
    }

    private Notification(
        NotificationId id,
        NotificationKey key,
        TopicName topic,
        NotificationKind kind,
        NotificationMessage message,
        DateTimeOffset createdAtUtc)
        : this(
            id: id,
            key: key,
            topic: topic,
            kind: kind,
            message: message)
    {
        Delivery = NotificationDelivery.Start(createdAtUtc: createdAtUtc);
    }

    public NotificationKey Key { get; private set; }
    public TopicName Topic { get; private set; }
    public NotificationKind Kind { get; private set; }
    public NotificationMessage Message { get; private set; }
    public NotificationDelivery Delivery { get; private set; }

    public static Result<Notification> Create(
        string key,
        string topic,
        NotificationKind kind,
        string message,
        DateTimeOffset createdAtUtc)
    {
        var keyResult = NotificationKey.Create(value: key);
        var topicResult = TopicName.Create(value: topic);
        var messageResult = NotificationMessage.Create(value: message);
        var validationResult = Result.Combine(
            keyResult,
            topicResult,
            messageResult);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Notification>(
                errors: validationResult.Errors);
        }

        return Result.Success(
            value: new Notification(
                id: NotificationId.New(),
                key: keyResult.Value,
                topic: topicResult.Value,
                kind: kind,
                message: messageResult.Value,
                createdAtUtc: createdAtUtc));
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Delivery = Delivery.MarkProcessing(nowUtc: now);
    }

    public void MarkSent(DateTimeOffset now)
    {
        Delivery = Delivery.MarkSent(nowUtc: now);
    }

    public void Reschedule(DateTimeOffset availableAtUtc, string error)
    {
        Delivery = Delivery.Reschedule(
            availableAtUtc: availableAtUtc,
            error: error);
    }

    public void MarkFailed(DateTimeOffset now, string error)
    {
        Delivery = Delivery.MarkFailed(
            nowUtc: now,
            error: error);
    }
}
