using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

public sealed partial record NotificationKey
{
    public const int Length = 64;

    private NotificationKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<NotificationKey> Create(string value)
    {
        if (!NotificationKeyRegex().IsMatch(value))
        {
            return Result.Failure<NotificationKey>(
                Error.Validation("Notification key must be a lowercase SHA-256 value."));
        }

        return Result.Success(new NotificationKey(value));
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex NotificationKeyRegex();
}
