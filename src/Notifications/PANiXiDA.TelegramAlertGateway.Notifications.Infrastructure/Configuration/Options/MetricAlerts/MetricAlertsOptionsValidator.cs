using System.Text.Encodings.Web;

using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;

internal sealed class MetricAlertsOptionsValidator : IValidateOptions<MetricAlertsOptions>
{
    public ValidateOptionsResult Validate(string? name, MetricAlertsOptions options)
    {
        return IsValidUrl(options.AlertmanagerUrl, 350) && IsValidUrl(options.GrafanaDashboardUrl, 1000)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Metric alert links must be absolute HTTP(S) URLs within their HTML budgets: Alertmanager 350, Grafana 1000.");
    }

    private static bool IsValidUrl(string value, int maxHtmlLength)
    {
        return string.IsNullOrWhiteSpace(value)
               || (HtmlEncoder.Default.Encode(value).Length <= maxHtmlLength
                   && Uri.TryCreate(uriString: value, uriKind: UriKind.Absolute, result: out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
    }
}
