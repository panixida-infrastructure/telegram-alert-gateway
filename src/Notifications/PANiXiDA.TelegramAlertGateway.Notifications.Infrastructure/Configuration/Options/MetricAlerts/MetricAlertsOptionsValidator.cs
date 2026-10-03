using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;

internal sealed class MetricAlertsOptionsValidator : IValidateOptions<MetricAlertsOptions>
{
    public ValidateOptionsResult Validate(string? name, MetricAlertsOptions options)
    {
        return IsValidUrl(options.AlertmanagerUrl) && IsValidUrl(options.GrafanaDashboardUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Metric alert links must be absolute HTTP(S) URLs of at most 300 characters.");
    }

    private static bool IsValidUrl(string value)
    {
        return string.IsNullOrWhiteSpace(value)
               || (value.Length <= 300
                   && Uri.TryCreate(uriString: value, uriKind: UriKind.Absolute, result: out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
    }
}
