using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;

internal sealed class VictoriaLogsOptionsValidator : IValidateOptions<VictoriaLogsOptions>
{
    public ValidateOptionsResult Validate(string? name, VictoriaLogsOptions options)
    {
        var isValid = Uri.TryCreate(
                          uriString: options.Endpoint,
                          uriKind: UriKind.Absolute,
                          result: out _)
                      && !string.IsNullOrWhiteSpace(options.Query)
                      && options.PollIntervalSeconds > 0
                      && options.WindowSeconds > 0
                      && options.IngestionDelaySeconds >= 0
                      && options.MaxEntriesPerWindow > 0;

        return isValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "VictoriaLogs endpoint, query, and positive polling limits are required.");
    }
}
