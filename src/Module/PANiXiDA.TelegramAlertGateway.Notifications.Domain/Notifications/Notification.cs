using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

public sealed class Notification : AggregateRoot<NotificationId>
{
    public const int MaxMessageLength = 3900;
    public const int MaxErrorLength = 2000;

    private Notification(
        NotificationId id,
        NotificationKey key,
        TopicName topic,
        NotificationKind kind,
        string message,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Key = key;
        Topic = topic;
        Kind = kind;
        Message = message;
        Status = NotificationStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        AvailableAtUtc = createdAtUtc;
    }

    public NotificationKey Key { get; private set; }
    public TopicName Topic { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Message { get; private set; }
    public NotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset AvailableAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static Result<Notification> Create(
        string key,
        string topic,
        NotificationKind kind,
        string message,
        DateTimeOffset createdAtUtc)
    {
        var keyResult = NotificationKey.Create(key);
        var topicResult = TopicName.Create(topic);

        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
        {
            return Result.Failure<Notification>(
                Error.Validation($"Message must contain at most {MaxMessageLength} characters."));
        }

        var validationResult = Result.Combine(keyResult, topicResult);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Notification>(validationResult.Errors);
        }

        return Result.Success(
            new Notification(
                NotificationId.New(),
                keyResult.Value,
                topicResult.Value,
                kind,
                message,
                createdAtUtc));
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = NotificationStatus.Processing;
        Attempts++;
        AvailableAtUtc = now;
        LastError = null;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        SentAtUtc = now;
        LastError = null;
    }

    public void Reschedule(DateTimeOffset availableAtUtc, string error)
    {
        Status = NotificationStatus.Pending;
        AvailableAtUtc = availableAtUtc;
        LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }

    public void MarkFailed(DateTimeOffset now, string error)
    {
        Status = NotificationStatus.Failed;
        AvailableAtUtc = now;
        LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}
