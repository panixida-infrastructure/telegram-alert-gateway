using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.NotificationRetention;

internal sealed class NotificationRetentionOptionsValidator
    : IValidateOptions<NotificationRetentionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        NotificationRetentionOptions options)
    {
        var isValid = options.SentRetentionDays > 0
                      && options.FailedRetentionDays >= options.SentRetentionDays
                      && options.BatchSize is > 0 and <= 10000
                      && options.CleanupIntervalHours is > 0 and <= 168;

        return isValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Notification retention periods, batch size, and cleanup interval are invalid.");
    }
}
