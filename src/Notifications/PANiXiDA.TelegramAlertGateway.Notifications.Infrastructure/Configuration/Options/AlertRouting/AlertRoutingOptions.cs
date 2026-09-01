namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.AlertRouting;

public sealed class AlertRoutingOptions
{
    public const string SectionName = "AlertRouting";

    public string DefaultTopic { get; init; } = "unclassified";
    public List<AlertRoutingRule> Rules { get; init; } = [];
}

public sealed class AlertRoutingRule
{
    public string Topic { get; init; } = string.Empty;
    public List<string> Matches { get; init; } = [];
}
