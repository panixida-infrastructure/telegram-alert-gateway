using System.Text;
using System.Text.Encodings.Web;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

internal sealed class TelegramNotificationComposer(
    ITopicRouter topicRouter,
    IOptions<VictoriaLogsOptions> victoriaLogsOptions)
    : INotificationComposer
{
    private const string Separator = "────────────";
    private const string ResolvedStatus = "resolved";
    private const string LinkOpeningTag = "🔗 <a href=\"";
    private const int MaxAlertBlockLength = 2600;
    private const int PageContentLimit = 3400;
    private static readonly TimeSpan MetricDeliveryDeduplicationWindow = TimeSpan.FromMinutes(5);

    private readonly VictoriaLogsOptions _victoriaLogsOptions = victoriaLogsOptions.Value;

    public IReadOnlyList<ComposedNotification> ComposeMetricAlerts(
        string status,
        string externalUrl,
        IReadOnlyList<AlertmanagerAlert> alerts,
        DateTimeOffset receivedAtUtc)
    {
        var result = new List<ComposedNotification>();
        var deliveryWindow = receivedAtUtc.UtcTicks / MetricDeliveryDeduplicationWindow.Ticks;

        foreach (var topicGroup in alerts
                     .GroupBy(alert => topicRouter.Route(alert.Labels), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var orderedAlerts = topicGroup
                .OrderBy(alert => alert.Fingerprint, StringComparer.Ordinal)
                .ToArray();
            var blocks = orderedAlerts.Select(BuildMetricAlertBlock).ToArray();
            var pages = Paginate(blocks);

            for (var index = 0; index < pages.Count; index++)
            {
                var normalizedStatus = string.Equals(
                    status,
                    ResolvedStatus,
                    StringComparison.OrdinalIgnoreCase)
                    ? ResolvedStatus
                    : "firing";
                var header = normalizedStatus == ResolvedStatus
                    ? "✅ <b>Alerts resolved</b>"
                    : "🔥 <b>Alerts firing</b>";
                var pageLabel = pages.Count > 1
                    ? $" · page {index + 1}/{pages.Count}"
                    : string.Empty;
                var body = new StringBuilder()
                    .AppendLine(header)
                    .Append("📊 <b>")
                    .Append(orderedAlerts.Length)
                    .Append(" alert(s)</b>")
                    .AppendLine(pageLabel)
                    .AppendLine()
                    .Append(pages[index]);

                if (!string.IsNullOrWhiteSpace(externalUrl))
                {
                    body.AppendLine()
                        .Append(LinkOpeningTag)
                        .Append(Html(externalUrl))
                        .Append("\">Alertmanager</a>");
                }

                var alertOccurrences = string.Join(
                    ',',
                    orderedAlerts.Select(alert => string.Join(
                        '|',
                        alert.Fingerprint,
                        alert.Status,
                        alert.StartsAt.UtcTicks,
                        alert.EndsAt?.UtcTicks)));
                var key = NotificationKeyFactory.Create(
                    $"metric|{deliveryWindow}|{topicGroup.Key}|{normalizedStatus}|{alertOccurrences}|{index}");

                result.Add(new ComposedNotification(key, topicGroup.Key, body.ToString()));
            }
        }

        return result;
    }

    public ComposedNotification ComposeLogEvent(
        DateTimeOffset windowStartUtc,
        LogEvent logEvent)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["service"] = logEvent.Service,
            ["namespace"] = logEvent.Namespace,
            ["container"] = logEvent.Container,
            ["alert_owner"] = logEvent.Owner ?? string.Empty
        };
        var topic = topicRouter.Route(dimensions);
        var location = string.Join(
            '/',
            new[] { logEvent.Namespace, logEvent.Container }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var message = new StringBuilder()
            .Append("🔴 <b>")
            .Append(Html(logEvent.Severity.ToUpperInvariant()))
            .Append(" · ")
            .Append(Html(Truncate(logEvent.Service, 180)))
            .AppendLine("</b>");

        if (!string.IsNullOrWhiteSpace(location))
        {
            message.Append("📦 ").AppendLine(Html(Truncate(location, 250)));
        }

        message.Append("🕒 ")
            .Append(logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));

        if (logEvent.Occurrences > 1)
        {
            message.Append(" · repeated <b>")
                .Append(logEvent.Occurrences)
                .Append(" times</b> in a ")
                .Append(FormatWindowDuration(_victoriaLogsOptions.WindowSeconds))
                .Append(" log window");
        }

        message.AppendLine().AppendLine();

        var messageBudget = string.IsNullOrWhiteSpace(logEvent.StackTrace) ? 2200 : 1400;
        message.Append("<pre>")
            .Append(Html(Truncate(logEvent.Message, messageBudget)))
            .AppendLine("</pre>");

        if (!string.IsNullOrWhiteSpace(logEvent.ExceptionType))
        {
            message.Append("⚠️ <b>")
                .Append(Html(Truncate(logEvent.ExceptionType, 180)))
                .AppendLine("</b>");
        }

        if (!string.IsNullOrWhiteSpace(logEvent.StackTrace))
        {
            message.Append("<pre>")
                .Append(Html(Truncate(logEvent.StackTrace, 750)))
                .AppendLine("</pre>");
        }

        if (!string.IsNullOrWhiteSpace(logEvent.TraceId))
        {
            message.Append("🔎 trace: <code>")
                .Append(Html(Truncate(logEvent.TraceId, 180)))
                .AppendLine("</code>");
        }

        if (!string.IsNullOrWhiteSpace(_victoriaLogsOptions.GrafanaLogsUrl))
        {
            message.Append(LinkOpeningTag)
                .Append(Html(Truncate(_victoriaLogsOptions.GrafanaLogsUrl, 500)))
                .Append("\">Logs</a>");
        }

        var key = NotificationKeyFactory.Create(
            $"log|{windowStartUtc.UtcTicks}|{logEvent.Fingerprint}");

        return new ComposedNotification(key, topic, message.ToString());
    }

    private static string FormatWindowDuration(int seconds)
    {
        if (seconds % 3600 == 0)
        {
            return $"{seconds / 3600}-hour";
        }

        if (seconds % 60 == 0)
        {
            return $"{seconds / 60}-minute";
        }

        return $"{seconds}-second";
    }

    private static string BuildMetricAlertBlock(AlertmanagerAlert alert)
    {
        var isResolved = string.Equals(alert.Status, ResolvedStatus, StringComparison.OrdinalIgnoreCase);
        var alertName = GetValue(alert.Labels, "alertname") ?? "unnamed-alert";
        var severity = GetValue(alert.Labels, "severity") ?? "warning";
        var owner = GetValue(alert.Labels, "service", "service_name", "job", "namespace")
            ?? GetValue(alert.Labels, "alert_owner")
            ?? "unclassified";
        var summary = GetValue(alert.Annotations, "summary") ?? "No summary provided.";
        var description = GetValue(alert.Annotations, "description");
        var dashboardUrl = GetValue(alert.Annotations, "dashboard_url", "logs_url")
            ?? alert.GeneratorUrl;

        var builder = new StringBuilder()
            .Append(isResolved ? "✅ " : "🔥 ")
            .Append("<b>")
            .Append(Html(Truncate(alertName, 180)))
            .Append("</b> · ")
            .AppendLine(Html(Truncate(severity.ToUpperInvariant(), 40)))
            .Append("📦 ")
            .AppendLine(Html(Truncate(owner, 180)))
            .Append("📝 ")
            .AppendLine(Html(Truncate(summary, 600)));

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append("<pre>")
                .Append(Html(Truncate(description, 850)))
                .AppendLine("</pre>");
        }

        if (!string.IsNullOrWhiteSpace(dashboardUrl))
        {
            builder.Append(LinkOpeningTag)
                .Append(Html(Truncate(dashboardUrl, 500)))
                .AppendLine("\">Open details</a>");
        }

        var block = builder.ToString();
        return block.Length <= MaxAlertBlockLength
            ? block
            : BuildMetricAlertBlockWithoutDescription(
                isResolved,
                alertName,
                severity,
                owner,
                summary,
                dashboardUrl);
    }

    private static string BuildMetricAlertBlockWithoutDescription(
        bool isResolved,
        string alertName,
        string severity,
        string owner,
        string summary,
        string? dashboardUrl)
    {
        var builder = new StringBuilder()
            .Append(isResolved ? "✅ " : "🔥 ")
            .Append("<b>")
            .Append(Html(Truncate(alertName, 180)))
            .Append("</b> · ")
            .AppendLine(Html(Truncate(severity.ToUpperInvariant(), 40)))
            .Append("📦 ")
            .AppendLine(Html(Truncate(owner, 180)))
            .Append("📝 ")
            .AppendLine(Html(Truncate(summary, 600)))
            .AppendLine("<i>Description omitted because the alert is too long.</i>");

        if (!string.IsNullOrWhiteSpace(dashboardUrl))
        {
            builder.Append(LinkOpeningTag)
                .Append(Html(Truncate(dashboardUrl, 500)))
                .AppendLine("\">Open details</a>");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> Paginate(IReadOnlyList<string> blocks)
    {
        var pages = new List<string>();
        var current = new StringBuilder();

        foreach (var block in blocks)
        {
            var requiredLength = block.Length + (current.Length == 0 ? 0 : Separator.Length + 2);
            if (current.Length > 0 && current.Length + requiredLength > PageContentLimit)
            {
                pages.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.AppendLine().AppendLine(Separator);
            }

            current.Append(block);
        }

        if (current.Length > 0)
        {
            pages.Add(current.ToString());
        }

        return pages;
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Html(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }

}
