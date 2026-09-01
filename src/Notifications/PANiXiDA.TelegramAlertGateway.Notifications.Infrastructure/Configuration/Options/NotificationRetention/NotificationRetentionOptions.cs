namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.NotificationRetention;

public sealed class NotificationRetentionOptions
{
    public const string SectionName = "NotificationRetention";

    public int SentRetentionDays { get; init; } = 14;
    public int FailedRetentionDays { get; init; } = 30;
    public int BatchSize { get; init; } = 500;
    public int CleanupIntervalHours { get; init; } = 24;
}
