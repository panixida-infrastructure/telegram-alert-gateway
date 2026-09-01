namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

public sealed class NotificationMessage : ValueObject
{
    public const int MaxLength = 3900;

    private NotificationMessage(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<NotificationMessage> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return Result.Failure<NotificationMessage>(
                error: Error.Validation(
                        message: $"Message must contain at most {MaxLength} characters.")
                    .WithField(nameof(NotificationMessage)));
        }

        return Result.Success(
            value: new NotificationMessage(value: value));
    }

    public override string ToString()
    {
        return Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
