namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;

public sealed class MetricAlertsOptions
{
    public const string SectionName = "MetricAlerts";

    public string AlertmanagerUrl { get; init; } = string.Empty;
    public string GrafanaDashboardUrl { get; init; } = string.Empty;
}
