using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

public sealed class NotificationDelivery : ValueObject
{
    public const int MaxLength = 2000;

    private NotificationDelivery(
        NotificationStatus status,
        int attempts,
        DateTimeOffset createdAtUtc,
        DateTimeOffset availableAtUtc,
        DateTimeOffset? sentAtUtc,
        string? lastError)
    {
        Status = status;
        Attempts = attempts;
        CreatedAtUtc = createdAtUtc;
        AvailableAtUtc = availableAtUtc;
        SentAtUtc = sentAtUtc;
        LastError = lastError;
    }

    public NotificationStatus Status { get; }
    public int Attempts { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset AvailableAtUtc { get; }
    public DateTimeOffset? SentAtUtc { get; }
    public string? LastError { get; }

    public static NotificationDelivery Start(DateTimeOffset createdAtUtc)
    {
        return new NotificationDelivery(
            status: NotificationStatus.Pending,
            attempts: 0,
            createdAtUtc: createdAtUtc,
            availableAtUtc: createdAtUtc,
            sentAtUtc: null,
            lastError: null);
    }

    public NotificationDelivery MarkProcessing(DateTimeOffset nowUtc)
    {
        return new NotificationDelivery(
            status: NotificationStatus.Processing,
            attempts: Attempts + 1,
            createdAtUtc: CreatedAtUtc,
            availableAtUtc: nowUtc,
            sentAtUtc: SentAtUtc,
            lastError: null);
    }

    public NotificationDelivery MarkSent(DateTimeOffset nowUtc)
    {
        return new NotificationDelivery(
            status: NotificationStatus.Sent,
            attempts: Attempts,
            createdAtUtc: CreatedAtUtc,
            availableAtUtc: AvailableAtUtc,
            sentAtUtc: nowUtc,
            lastError: null);
    }

    public NotificationDelivery Reschedule(
        DateTimeOffset availableAtUtc,
        string error)
    {
        return new NotificationDelivery(
            status: NotificationStatus.Pending,
            attempts: Attempts,
            createdAtUtc: CreatedAtUtc,
            availableAtUtc: availableAtUtc,
            sentAtUtc: SentAtUtc,
            lastError: NormalizeError(error));
    }

    public NotificationDelivery MarkFailed(
        DateTimeOffset nowUtc,
        string error)
    {
        return new NotificationDelivery(
            status: NotificationStatus.Failed,
            attempts: Attempts,
            createdAtUtc: CreatedAtUtc,
            availableAtUtc: nowUtc,
            sentAtUtc: SentAtUtc,
            lastError: NormalizeError(error));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Status;
        yield return Attempts;
        yield return CreatedAtUtc;
        yield return AvailableAtUtc;
        yield return SentAtUtc;
        yield return LastError;
    }

    private static string NormalizeError(string error)
    {
        return error.Length <= MaxLength
            ? error
            : error[..MaxLength];
    }
}
