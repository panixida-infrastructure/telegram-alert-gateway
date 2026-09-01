using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

public sealed partial class TopicName : ValueObject
{
    public const int MaxLength = 64;

    private TopicName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<TopicName> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<TopicName>(Error.Validation("Topic name cannot be empty."));
        }

        if (value.Length > MaxLength || !TopicNameRegex().IsMatch(value))
        {
            return Result.Failure<TopicName>(
                Error.Validation("Topic name must use lower-kebab-case."));
        }

        return Result.Success(value: new TopicName(value: value));
    }

    public override string ToString()
    {
        return Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TopicNameRegex();
}
