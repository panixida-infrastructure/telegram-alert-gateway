using System.Net;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

public sealed partial class NotificationMessage : ValueObject
{
    public const int MaxLength = 4096;

    private NotificationMessage(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<NotificationMessage> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || GetTextLength(value) is 0 or > MaxLength)
        {
            return Result.Failure<NotificationMessage>(
                error: Error.Validation(
                        message: $"Message must contain at most {MaxLength} characters after HTML parsing.")
                    .WithField(nameof(NotificationMessage)));
        }

        return Result.Success(
            value: new NotificationMessage(value: value));
    }

    public override string ToString()
    {
        return Value;
    }

    public static int GetTextLength(string html)
    {
        return WebUtility.HtmlDecode(HtmlTagRegex().Replace(html, string.Empty)).EnumerateRunes().Count();
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
