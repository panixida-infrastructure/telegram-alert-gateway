using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
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
    private const string PreformattedTextOpeningTag = "<pre>";
    private const string PreformattedTextClosingTag = "</pre>";
    private const string VictoriaLogsDataSourceType = "victoriametrics-logs-datasource";
    private const string VictoriaLogsDataSourceUid = "victorialogs";
    private const int MaxAlertBlockLength = 2600;
    private const int MaxLogFieldsLength = 700;
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

                result.Add(new ComposedNotification(
                    Key: key,
                    Topic: topicGroup.Key,
                    Message: body.ToString()));
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
        var logsUrl = BuildGrafanaLogsUrl(windowStartUtc, logEvent.StreamId);
        var message = BuildLogMessage(
            windowStartUtc: windowStartUtc,
            logEvent: logEvent,
            logsUrl: logsUrl,
            messageBudget: GetPreferredMessageBudget(logEvent),
            fieldsBudget: MaxLogFieldsLength,
            stackTraceBudget: 750);
        if (message.Length > NotificationMessage.MaxLength)
        {
            message = BuildLogMessage(
                windowStartUtc: windowStartUtc,
                logEvent: logEvent,
                logsUrl: logsUrl,
                messageBudget: 700,
                fieldsBudget: 450,
                stackTraceBudget: 250);
        }

        if (message.Length > NotificationMessage.MaxLength)
        {
            message = BuildLogMessage(
                windowStartUtc: windowStartUtc,
                logEvent: logEvent,
                logsUrl: logsUrl,
                messageBudget: 500,
                fieldsBudget: 250,
                stackTraceBudget: 0);
        }

        if (message.Length > NotificationMessage.MaxLength)
        {
            message = BuildLogMessage(
                windowStartUtc: windowStartUtc,
                logEvent: logEvent,
                logsUrl: null,
                messageBudget: 350,
                fieldsBudget: 0,
                stackTraceBudget: 0);
        }

        var key = NotificationKeyFactory.Create(
            $"log|{windowStartUtc.UtcTicks}|{logEvent.Fingerprint}");

        return new ComposedNotification(
            Key: key,
            Topic: topic,
            Message: message);
    }

    private string BuildLogMessage(
        DateTimeOffset windowStartUtc,
        LogEvent logEvent,
        string? logsUrl,
        int messageBudget,
        int fieldsBudget,
        int stackTraceBudget)
    {
        var location = string.Join(
            '/',
            new[] { logEvent.Namespace, logEvent.Container }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var message = new StringBuilder()
            .Append("🔴 <b>")
            .Append(HtmlTruncate(logEvent.Severity.ToUpperInvariant(), 40))
            .Append(" · ")
            .Append(HtmlTruncate(logEvent.Service, 180))
            .AppendLine("</b>");

        if (!string.IsNullOrWhiteSpace(location))
        {
            message.Append("📦 ").AppendLine(HtmlTruncate(location, 250));
        }

        message.Append("🕒 ")
            .AppendLine(logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));

        if (logEvent.Occurrences > 1)
        {
            var windowEndUtc = windowStartUtc.AddSeconds(_victoriaLogsOptions.WindowSeconds);
            message.Append("📊 At least <b>")
                .Append(logEvent.Occurrences)
                .Append(" matching events</b> in the ")
                .Append(FormatWindowDuration(_victoriaLogsOptions.WindowSeconds))
                .Append(" window ")
                .Append(windowStartUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append('–')
                .Append(windowEndUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))
                .AppendLine();
        }

        message.AppendLine();
        message.Append(PreformattedTextOpeningTag)
            .Append(HtmlTruncate(logEvent.Message, messageBudget))
            .AppendLine(PreformattedTextClosingTag);

        if (fieldsBudget > 0 && logEvent.Fields.Count > 0)
        {
            message.AppendLine("🏷 <b>Fields</b>")
                .Append(PreformattedTextOpeningTag)
                .Append(HtmlTruncate(FormatFields(logEvent.Fields), fieldsBudget))
                .AppendLine(PreformattedTextClosingTag);
        }

        if (!string.IsNullOrWhiteSpace(logEvent.ExceptionType))
        {
            message.Append("⚠️ <b>")
                .Append(HtmlTruncate(logEvent.ExceptionType, 180))
                .AppendLine("</b>");
        }

        if (stackTraceBudget > 0 && !string.IsNullOrWhiteSpace(logEvent.StackTrace))
        {
            message.Append(PreformattedTextOpeningTag)
                .Append(HtmlTruncate(logEvent.StackTrace, stackTraceBudget))
                .AppendLine(PreformattedTextClosingTag);
        }

        if (!string.IsNullOrWhiteSpace(logEvent.TraceId))
        {
            message.Append("🔎 trace: <code>")
                .Append(HtmlTruncate(logEvent.TraceId, 180))
                .AppendLine("</code>");
        }

        if (!string.IsNullOrWhiteSpace(logsUrl))
        {
            message.Append(LinkOpeningTag)
                .Append(Html(logsUrl))
                .Append("\">Logs for this source and window</a>");
        }

        return message.ToString();
    }

    private static int GetPreferredMessageBudget(LogEvent logEvent)
    {
        var hasFields = logEvent.Fields.Count > 0;
        var hasStackTrace = !string.IsNullOrWhiteSpace(logEvent.StackTrace);
        return (hasFields, hasStackTrace) switch
        {
            (true, true) => 900,
            (true, false) => 1400,
            (false, true) => 1400,
            (false, false) => 2200
        };
    }

    private string? BuildGrafanaLogsUrl(
        DateTimeOffset windowStartUtc,
        string? streamId)
    {
        if (string.IsNullOrWhiteSpace(_victoriaLogsOptions.GrafanaLogsUrl))
        {
            return null;
        }

        if (!IsVictoriaLogsStreamId(streamId)
            || !Uri.TryCreate(
                uriString: _victoriaLogsOptions.GrafanaLogsUrl,
                uriKind: UriKind.Absolute,
                result: out var configuredUri))
        {
            return _victoriaLogsOptions.GrafanaLogsUrl;
        }

        var query = $"_stream_id:{streamId}";
        var windowEndUtc = windowStartUtc.AddSeconds(_victoriaLogsOptions.WindowSeconds);
        var panes = new Dictionary<string, object>
        {
            ["logs"] = new
            {
                datasource = VictoriaLogsDataSourceUid,
                queries = new[]
                {
                    new
                    {
                        refId = "A",
                        datasource = new
                        {
                            type = VictoriaLogsDataSourceType,
                            uid = VictoriaLogsDataSourceUid
                        },
                        editorMode = "code",
                        expr = query,
                        query
                    }
                },
                range = new
                {
                    from = windowStartUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    to = windowEndUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
                }
            }
        };
        var builder = new UriBuilder(configuredUri)
        {
            Fragment = string.Empty,
            Query = $"panes={Uri.EscapeDataString(JsonSerializer.Serialize(panes))}&schemaVersion=1"
        };
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsVictoriaLogsStreamId(string? streamId)
    {
        return !string.IsNullOrWhiteSpace(streamId)
               && streamId.Length <= 256
               && streamId.All(Uri.IsHexDigit);
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

    private static string FormatFields(IReadOnlyDictionary<string, string> fields)
    {
        var result = new StringBuilder();
        foreach (var field in fields.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Append(field.Key.Trim())
                .Append(": ")
                .AppendLine(field.Value.Trim());
        }

        return result.ToString().TrimEnd();
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
            builder.Append(PreformattedTextOpeningTag)
                .Append(Html(Truncate(description, 850)))
                .AppendLine(PreformattedTextClosingTag);
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
                isResolved: isResolved,
                alertName: alertName,
                severity: severity,
                owner: owner,
                summary: summary,
                dashboardUrl: dashboardUrl);
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

    private static string HtmlTruncate(string value, int maxEncodedLength)
    {
        var encoded = Html(value);
        if (encoded.Length <= maxEncodedLength)
        {
            return encoded;
        }

        var minimum = 0;
        var maximum = value.Length;
        while (minimum < maximum)
        {
            var candidate = minimum + ((maximum - minimum + 1) / 2);
            if (Html(value[..candidate]).Length < maxEncodedLength)
            {
                minimum = candidate;
            }
            else
            {
                maximum = candidate - 1;
            }
        }

        if (minimum > 0 && char.IsHighSurrogate(value[minimum - 1]))
        {
            minimum--;
        }

        return string.Concat(Html(value[..minimum]), "…");
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
