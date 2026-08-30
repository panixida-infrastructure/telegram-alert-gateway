namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;

public sealed class VictoriaLogsOptions
{
    public const string SectionName = "VictoriaLogs";

    public string Endpoint { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Query { get; init; } =
        "severity_text:(i(error) OR i(fatal) OR i(critical))"
        + " OR severity:(i(error) OR i(fatal) OR i(critical))"
        + " OR level:(i(error) OR i(fatal) OR i(critical))"
        + " OR \"log.level\":(i(error) OR i(fatal) OR i(critical))"
        + " OR LogLevel:(i(error) OR i(fatal) OR i(critical))"
        + " OR (i(error) OR i(exception) OR i(fatal) OR i(critical) OR i(failed) OR i(failure))";
    public int PollIntervalSeconds { get; init; } = 60;
    public int IngestionDelaySeconds { get; init; } = 15;
    public int MaxEntriesPerWindow { get; init; } = 10000;
    public string GrafanaLogsUrl { get; init; } = string.Empty;
    public List<string> ExcludedServices { get; init; } = ["telegram-alert-gateway"];
}
