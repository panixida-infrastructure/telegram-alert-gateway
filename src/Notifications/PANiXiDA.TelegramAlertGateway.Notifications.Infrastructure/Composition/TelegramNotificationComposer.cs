using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

internal sealed class TelegramNotificationComposer(
    IOptions<MetricAlertsOptions> metricAlertsOptions,
    ITopicRouter topicRouter,
    IOptions<VictoriaLogsOptions> victoriaLogsOptions)
    : INotificationComposer
{
    private const string Separator = "• • •";
    private const string ResolvedStatus = "resolved";
    private const string LinkOpeningTag = "🔗 <a href=\"";
    private const string PreformattedTextOpeningTag = "<pre>";
    private const string PreformattedTextClosingTag = "</pre>";
    private const string VictoriaLogsDataSourceType = "victoriametrics-logs-datasource";
    private const string VictoriaLogsDataSourceUid = "victorialogs";
    private const string VictoriaTracesDataSourceType = "jaeger";
    private const string VictoriaTracesDataSourceUid = "victoriatraces";
    private const string LogRecordUidFieldName = "log.record.uid";
    private const string ExceptionHResultFieldName = "exception.hresult";
    private const string ExceptionSourceFieldName = "exception.source";
    private static readonly string[] ExceptionMessageFieldNames = ["exception.message", "error", "err"];
    private static readonly TimeSpan MetricDeliveryDeduplicationWindow = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions LogDetailsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly MetricAlertsOptions _metricAlertsOptions = metricAlertsOptions.Value;
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
            var pages = BuildMetricPages(status: status, externalUrl: externalUrl, alerts: orderedAlerts);

            for (var index = 0; index < pages.Count; index++)
            {
                var normalizedStatus = string.Equals(status, ResolvedStatus, StringComparison.OrdinalIgnoreCase)
                    ? ResolvedStatus : "firing";
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
                    Message: pages[index]));
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
        var logsUrl = BuildGrafanaLogsUrl(windowStartUtc, logEvent);
        var footer = new StringBuilder();
        AppendLogLinks(message: footer, windowStartUtc: windowStartUtc, logEvent: logEvent, logsUrl: logsUrl);
        var header = BuildLogHeader(windowStartUtc: windowStartUtc, logEvent: logEvent, valueBudget: int.MaxValue);
        var exceptionMessage = GetValue(logEvent.Fields, ExceptionMessageFieldNames);
        var hResult = GetExceptionHResult(logEvent.Fields);
        var source = GetValue(logEvent.Fields, ExceptionSourceFieldName);
        var fields = logEvent.Fields
            .Where(field => !(ExceptionMessageFieldNames.Contains(field.Key, StringComparer.OrdinalIgnoreCase)
                             && string.Equals(field.Value, exceptionMessage, StringComparison.Ordinal))
                            && !(hResult.HasValue && string.Equals(field.Key, ExceptionHResultFieldName, StringComparison.OrdinalIgnoreCase))
                            && !(source is not null && string.Equals(field.Key, ExceptionSourceFieldName, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(field => field.Key, field => field.Value);
        var exceptionsText = FormatExceptions(logEvent: logEvent, exceptionMessage: exceptionMessage, hResult: hResult, source: source, budget: int.MaxValue);
        var fieldsText = FormatFields(fields, int.MaxValue);
        var message = RenderLogMessage(header: header, text: logEvent.Message, exceptions: exceptionsText, fields: fieldsText, footer: footer.ToString());

        if (NotificationMessage.GetTextLength(message) > NotificationMessage.MaxLength)
        {
            // Only oversized alerts need shortened metadata. Keep space for their actual content.
            header = Fit(render: limit => BuildLogHeader(windowStartUtc: windowStartUtc, logEvent: logEvent, valueBudget: limit), budget: NotificationMessage.MaxLength / 4, html: true);
            var overhead = NotificationMessage.GetTextLength(RenderLogMessage(
                header: header, text: string.Empty, exceptions: exceptionsText is null ? null : string.Empty,
                fields: fieldsText is null ? null : string.Empty, footer: footer.ToString()));
            var available = NotificationMessage.MaxLength - overhead;
            var hasDetails = exceptionsText is not null || fieldsText is not null;
            var text = Truncate(logEvent.Message, hasDetails ? available / 2 : available);
            var remaining = available - TextLength(text);
            var exceptionsLength = TextLength(exceptionsText);
            var fieldsLength = TextLength(fieldsText);
            var exceptionBudget = Math.Min(exceptionsLength, (remaining + 1) / 2);
            var fieldsBudget = Math.Min(fieldsLength, remaining - exceptionBudget);
            fieldsText = FormatFields(fields, fieldsBudget);
            // A short block or whole-field omission releases its unused share to the other block.
            exceptionsText = FormatExceptions(logEvent: logEvent, exceptionMessage: exceptionMessage, hResult: hResult, source: source,
                budget: remaining - TextLength(fieldsText));
            fieldsText = FormatFields(fields, remaining - TextLength(exceptionsText));
            exceptionsText = FormatExceptions(logEvent: logEvent, exceptionMessage: exceptionMessage, hResult: hResult, source: source,
                budget: remaining - TextLength(fieldsText));
            text = Truncate(logEvent.Message, available
                - TextLength(fieldsText)
                - TextLength(exceptionsText));
            message = RenderLogMessage(header: header, text: text, exceptions: exceptionsText, fields: fieldsText, footer: footer.ToString());
        }

        var key = NotificationKeyFactory.Create($"log|{windowStartUtc.UtcTicks}|{logEvent.Fingerprint}");
        return new ComposedNotification(Key: key, Topic: topic, Message: message);
    }

    private string BuildLogHeader(DateTimeOffset windowStartUtc, LogEvent logEvent, int valueBudget)
    {
        var location = string.Join(
            '/',
            new[] { logEvent.Namespace, logEvent.Container }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var message = new StringBuilder()
            .Append("🔴 <b>")
            .Append(HtmlTruncate(logEvent.Severity.ToUpperInvariant(), valueBudget))
            .Append(" · ")
            .Append(HtmlTruncate(logEvent.Service, valueBudget))
            .AppendLine("</b>");

        if (!string.IsNullOrWhiteSpace(location))
        {
            message.Append("📦 ").AppendLine(HtmlTruncate(location, valueBudget));
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

        return message.AppendLine().ToString();
    }

    private static string RenderLogMessage(string header, string text, string? exceptions, string? fields, string footer)
    {
        var message = new StringBuilder(header).Append(PreformattedTextOpeningTag).Append("message: ").Append(Html(text));
        if (exceptions is not null)
        {
            message.AppendLine().AppendLine().Append(Html(exceptions));
        }
        if (fields is not null)
        {
            message.AppendLine().AppendLine().Append(Html(fields));
        }
        return message.AppendLine(PreformattedTextClosingTag).Append(footer).ToString();
    }

    private void AppendLogLinks(
        StringBuilder message,
        DateTimeOffset windowStartUtc,
        LogEvent logEvent,
        string? logsUrl)
    {
        var hasLogsLink = !string.IsNullOrWhiteSpace(logsUrl);
        var hasTrace = logEvent.Occurrences <= 1 && !string.IsNullOrWhiteSpace(logEvent.TraceId);
        if (!hasLogsLink && !hasTrace)
        {
            return;
        }

        message.AppendLine();
        if (!string.IsNullOrWhiteSpace(logsUrl))
        {
            message.Append(LinkOpeningTag)
                .Append(Html(logsUrl))
                .Append("\">")
                .Append(GetLogLinkLabel(logEvent))
                .AppendLine("</a>");
        }

        if (hasTrace)
        {
            AppendTrace(
                message: message,
                windowStartUtc: windowStartUtc,
                traceId: logEvent.TraceId,
                includeLink: logsUrl is not null);
        }
    }

    private static string GetLogLinkLabel(LogEvent logEvent)
    {
        if (logEvent.Occurrences > 1)
        {
            return logEvent.RecordUids is { Count: > 0 } ? "Logs" : "Logs for this source and window";
        }

        return GetValue(logEvent.Fields, LogRecordUidFieldName) is not null ? "Log" : "Logs for this source and window";
    }

    private void AppendTrace(
        StringBuilder message,
        DateTimeOffset windowStartUtc,
        string? traceId,
        bool includeLink)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return;
        }

        var traceUrl = includeLink ? BuildGrafanaTraceUrl(windowStartUtc, traceId) : null;
        message.Append("🔎 ");
        if (traceUrl is null)
        {
            message.Append("Trace: <code>").Append(HtmlTruncate(traceId, 180)).AppendLine("</code>");
            return;
        }

        message.Append("<a href=\"").Append(Html(traceUrl)).AppendLine("\">Trace</a>");
    }

    private string? BuildGrafanaLogsUrl(
        DateTimeOffset windowStartUtc,
        LogEvent logEvent)
    {
        if (string.IsNullOrWhiteSpace(_victoriaLogsOptions.GrafanaLogsUrl))
        {
            return null;
        }

        var groupQuery = LogGroupQuery.Create(windowStartUtc, logEvent);
        var recordUid = logEvent.Occurrences <= 1 ? GetValue(logEvent.Fields, LogRecordUidFieldName) : null;
        if ((groupQuery is null && recordUid is null && !IsVictoriaLogsStreamId(logEvent.StreamId))
            || !Uri.TryCreate(
                uriString: _victoriaLogsOptions.GrafanaLogsUrl,
                uriKind: UriKind.Absolute,
                result: out var configuredUri))
        {
            return _victoriaLogsOptions.GrafanaLogsUrl;
        }

        var query = groupQuery?.Query ?? (recordUid is not null
            ? $"{LogRecordUidFieldName}:={JsonSerializer.Serialize(recordUid)}"
            : $"_stream_id:{logEvent.StreamId}");
        return BuildGrafanaExploreUrl(
            windowStartUtc: windowStartUtc,
            configuredUri: configuredUri,
            paneKey: "logs",
            dataSourceUid: VictoriaLogsDataSourceUid,
            query: new
            {
                refId = "A",
                datasource = new { type = VictoriaLogsDataSourceType, uid = VictoriaLogsDataSourceUid },
                editorMode = "code",
                queryType = "instant",
                expr = query,
                query
            });
    }

    private string? BuildGrafanaTraceUrl(DateTimeOffset windowStartUtc, string traceId)
    {
        if (traceId.Length is not (16 or 32) || !traceId.All(Uri.IsHexDigit)
            || !Uri.TryCreate(
                uriString: _victoriaLogsOptions.GrafanaLogsUrl,
                uriKind: UriKind.Absolute,
                result: out var configuredUri)
            || (configuredUri.Scheme != Uri.UriSchemeHttp && configuredUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return BuildGrafanaExploreUrl(
            windowStartUtc: windowStartUtc,
            configuredUri: configuredUri,
            paneKey: "trace",
            dataSourceUid: VictoriaTracesDataSourceUid,
            query: new
            {
                refId = "A",
                datasource = new { type = VictoriaTracesDataSourceType, uid = VictoriaTracesDataSourceUid },
                query = traceId
            });
    }

    private string BuildGrafanaExploreUrl(
        DateTimeOffset windowStartUtc,
        Uri configuredUri,
        string paneKey,
        string dataSourceUid,
        object query)
    {
        var windowEndUtc = windowStartUtc.AddSeconds(_victoriaLogsOptions.WindowSeconds);
        var panes = new Dictionary<string, object>
        {
            [paneKey] = new
            {
                datasource = dataSourceUid,
                queries = new[] { query },
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

    private static string? FormatFields(Dictionary<string, string> fields, int budget)
    {
        if (fields.Count == 0)
        {
            return null;
        }
        var orderedFields = fields
            .OrderByDescending(item => string.Equals(item.Key, LogRecordUidFieldName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value.Trim());
        var full = SerializeFields(orderedFields);
        if (TextLength(full) <= budget)
        {
            return full;
        }

        var visible = new Dictionary<string, string>();
        foreach (var field in orderedFields)
        {
            visible.Add(field.Key, field.Value);
            var candidate = SerializeFields(visible) + Environment.NewLine + FormatOmittedFieldsNotice(fields.Count - visible.Count);
            if (TextLength(candidate) > budget)
            {
                visible.Remove(field.Key);
            }
        }
        var result = SerializeFields(visible) + Environment.NewLine + FormatOmittedFieldsNotice(fields.Count - visible.Count);
        return TextLength(result) <= budget ? result : Truncate(FormatOmittedFieldsNotice(fields.Count), budget);
    }

    private static string FormatOmittedFieldsNotice(int count)
    {
        return $"… ({count} fields omitted)";
    }

    private static string SerializeFields(IReadOnlyDictionary<string, string> fields)
    {
        return $"fields:{Environment.NewLine}{JsonSerializer.Serialize(fields, LogDetailsJsonOptions)}";
    }

    private static string? FormatExceptions(LogEvent logEvent, string? exceptionMessage, int? hResult, string? source, int budget)
    {
        var details = DotNetExceptionParser.Parse(logEvent.StackTrace)
                      ?? [new LogExceptionDetails(Depth: 0, ClassName: logEvent.ExceptionType, Message: exceptionMessage, StackTrace: logEvent.StackTrace)];

        string Render(int count, int valueBudget, int stackBudget) => RenderExceptions(
            details: details, exceptionType: logEvent.ExceptionType, hResult: hResult, source: source,
            count: count, valueBudget: valueBudget, stackBudget: stackBudget);

        var full = Render(count: details.Count, valueBudget: int.MaxValue, stackBudget: int.MaxValue);
        if (full.Length == 0)
        {
            return null;
        }
        if (TextLength(full) <= budget)
        {
            return full;
        }

        var count = details.Count;
        while (count > 1 && TextLength(Render(count: count, valueBudget: 0, stackBudget: 0)) > budget)
        {
            count--;
        }
        if (TextLength(Render(count: count, valueBudget: 0, stackBudget: 0)) > budget)
        {
            return Truncate($"… ({details.Count} exceptions omitted)", budget);
        }
        // Preserve type/message before allocating the remainder to stack traces.
        return TextLength(Render(count: count, valueBudget: int.MaxValue, stackBudget: 0)) <= budget
            ? Fit(limit => Render(count: count, valueBudget: int.MaxValue, stackBudget: limit), budget)
            : Fit(limit => Render(count: count, valueBudget: limit, stackBudget: 0), budget);
    }

    private static string RenderExceptions(IReadOnlyList<LogExceptionDetails> details, string? exceptionType,
        int? hResult, string? source, int count, int valueBudget, int stackBudget)
    {
        LogExceptionDetails[] visible = [.. details];
        if (count == 1)
        {
            visible = [details[0]];
        }
        else if (details.Count > count)
        {
            visible = [.. details.Take(count - 1), details[^1]];
        }
        var exceptions = visible.Select((detail, index) => FormatException(
                detail: index == 0 ? detail with { ClassName = exceptionType ?? detail.ClassName } : detail,
                hResult: index == 0 ? hResult : null, source: index == 0 ? source : null,
                messageBudget: valueBudget, stackTraceBudget: stackBudget))
            .Where(exception => exception.Count > 1).ToArray();
        if (exceptions.Length == 0)
        {
            return string.Empty;
        }
        var formatted = $"exceptions:{Environment.NewLine}{JsonSerializer.Serialize(exceptions, LogDetailsJsonOptions)}";
        return details.Count > count ? $"{formatted}{Environment.NewLine}… ({details.Count - count} exceptions omitted)" : formatted;
    }

    private static Dictionary<string, object> FormatException(
        LogExceptionDetails detail,
        int? hResult,
        string? source,
        int messageBudget,
        int stackTraceBudget)
    {
        var exception = new Dictionary<string, object> { ["Depth"] = detail.Depth };
        if (!string.IsNullOrWhiteSpace(detail.ClassName))
        {
            exception["ClassName"] = TruncateJsonValue(detail.ClassName, messageBudget);
        }

        if (!string.IsNullOrWhiteSpace(detail.Message))
        {
            exception["Message"] = TruncateJsonValue(detail.Message, messageBudget);
        }

        if (!string.IsNullOrWhiteSpace(detail.StackTrace))
        {
            exception["StackTraceString"] = stackTraceBudget > 0
                ? TruncateJsonValue(detail.StackTrace, stackTraceBudget)
                : "… (stack trace omitted)";
        }

        if (hResult.HasValue)
        {
            exception["HResult"] = hResult.Value;
        }

        if (source is not null)
        {
            exception["Source"] = TruncateJsonValue(source, messageBudget);
        }

        return exception;
    }

    private static int? GetExceptionHResult(IReadOnlyDictionary<string, string> fields)
    {
        return int.TryParse(
            s: GetValue(fields, ExceptionHResultFieldName),
            style: NumberStyles.Integer,
            provider: CultureInfo.InvariantCulture,
            result: out var hResult)
            ? hResult
            : null;
    }

    private static string TruncateJsonValue(string value, int maxEncodedLength)
    {
        if (TextLength(JsonSerializer.Serialize(value, LogDetailsJsonOptions)) <= maxEncodedLength)
        {
            return value;
        }

        var minimum = 0;
        var maximum = value.Length;
        while (minimum < maximum)
        {
            var candidate = minimum + ((maximum - minimum + 1) / 2);
            var prefixLength = GetUnicodePrefixLength(value, candidate);
            var shortened = value[..prefixLength] + "…";
            if (TextLength(JsonSerializer.Serialize(shortened, LogDetailsJsonOptions)) <= maxEncodedLength)
            {
                minimum = candidate;
            }
            else
            {
                maximum = candidate - 1;
            }
        }

        return value[..GetUnicodePrefixLength(value, minimum)] + "…";
    }

    private static int GetUnicodePrefixLength(string value, int length)
    {
        return length > 0 && length < value.Length
                          && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])
            ? length - 1
            : length;
    }

    private string BuildAlertmanagerLink(string externalUrl, string pageContent)
    {
        var url = string.IsNullOrWhiteSpace(_metricAlertsOptions.AlertmanagerUrl)
            ? externalUrl
            : _metricAlertsOptions.AlertmanagerUrl;
        if (!IsHttpUrl(url))
        {
            return string.Empty;
        }

        var spacing = pageContent.EndsWith($"</a>{Environment.NewLine}", StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;
        return $"{spacing}{LinkOpeningTag}{Html(url)}\">Alertmanager</a>";
    }

    private string BuildMetricAlertBlock(AlertmanagerAlert alert, int textBudget)
    {
        var isResolved = string.Equals(alert.Status, ResolvedStatus, StringComparison.OrdinalIgnoreCase);
        var alertName = GetValue(alert.Labels, "alertname") ?? "unnamed-alert";
        var severity = GetValue(alert.Labels, "severity") ?? "warning";
        var owner = GetValue(alert.Labels, "service", "service_name", "job", "namespace", "alert_owner") ?? "unclassified";
        var summary = GetValue(alert.Annotations, "summary") ?? "No summary provided.";
        var description = GetValue(alert.Annotations, "description");
        var dashboardUrl = GetValue(alert.Annotations, "dashboard_url", "logs_url") ?? _metricAlertsOptions.GrafanaDashboardUrl;
        var builder = new StringBuilder()
            .Append(isResolved ? "✅ " : "🔥 ").Append("<b>").Append(HtmlTruncate(alertName, textBudget))
            .Append("</b> · ").AppendLine(HtmlTruncate(severity.ToUpperInvariant(), textBudget))
            .Append("📦 ").AppendLine(HtmlTruncate(owner, textBudget))
            .Append(BuildMetricTarget(alert.Labels, textBudget))
            .Append("📝 ").AppendLine(HtmlTruncate(summary, textBudget));
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append("📖 ").AppendLine(HtmlTruncate(description, textBudget));
        }
        if (IsHttpUrl(dashboardUrl))
        {
            builder.AppendLine().Append(LinkOpeningTag).Append(Html(dashboardUrl)).AppendLine("\">Grafana</a>");
        }
        return builder.ToString();
    }

    private static string BuildMetricTarget(IReadOnlyDictionary<string, string> labels, int textBudget)
    {
        var builder = new StringBuilder();
        var endpoint = GetHttpOrigin(GetValue(labels, "http_url"));
        if (endpoint is not null)
        {
            builder.Append("🌐 ").AppendLine(FormatMetricAddress(address: endpoint, label: $"{endpoint.Host}:{endpoint.Port}", textBudget: textBudget));
        }

        var instance = GetValue(labels, "instance", "service_instance_id");
        if (instance is not null)
        {
            var address = GetHttpOrigin(instance) ?? GetInstanceOrigin(instance);
            var label = address is null ? instance : $"{address.Host}:{address.Port}";
            builder.Append("🖥 Instance: ").AppendLine(FormatMetricAddress(address: address, label: label, textBudget: textBudget));
        }

        return builder.ToString();
    }

    private static string FormatMetricAddress(Uri? address, string label, int textBudget)
    {
        var text = HtmlTruncate(label, textBudget);
        if (address is null)
        {
            return text;
        }

        var link = $"<a href=\"{Html(address.AbsoluteUri)}\">{text}</a>";
        return link;
    }

    private static Uri? GetHttpOrigin(string? value)
    {
        if (!Uri.TryCreate(uriString: value, uriKind: UriKind.Absolute, result: out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.HostNameType is UriHostNameType.Basic or UriHostNameType.Unknown)
        {
            return null;
        }

        return new UriBuilder { Scheme = uri.Scheme, Host = uri.Host, Port = uri.Port }.Uri;
    }

    private static Uri? GetInstanceOrigin(string instance)
    {
        var separator = instance.LastIndexOf(':');
        if (separator <= 0 || instance.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0
            || !int.TryParse(instance.AsSpan(separator + 1), out var port))
        {
            return null;
        }

        var scheme = port switch
        {
            80 or 8080 or 8081 or 9100 => Uri.UriSchemeHttp,
            443 or 8443 => Uri.UriSchemeHttps,
            _ => null
        };
        return scheme is null ? null : GetHttpOrigin($"{scheme}://{instance}");
    }

    private static bool IsHttpUrl([NotNullWhen(true)] string? value)
    {
        return Uri.TryCreate(uriString: value, uriKind: UriKind.Absolute, result: out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private List<string> BuildMetricPages(string status, string externalUrl, AlertmanagerAlert[] alerts)
    {
        var resolved = alerts.Count(alert => string.Equals(alert.Status, ResolvedStatus, StringComparison.OrdinalIgnoreCase));
        string Header(int index, int count)
        {
            var title = string.Equals(status, ResolvedStatus, StringComparison.OrdinalIgnoreCase)
                ? "✅ <b>Alerts resolved</b>" : "🔥 <b>Alerts firing</b>";
            var page = count > 1 ? $" · page {index + 1}/{count}" : string.Empty;
            return $"{title}{Environment.NewLine}📊 Firing: <b>{alerts.Length - resolved}</b> | Resolved: <b>{resolved}</b>{page}{Environment.NewLine}{Environment.NewLine}";
        }
        string Page(string content, int index, int count) => Header(index, count) + content + BuildAlertmanagerLink(externalUrl, content);

        var expectedCount = alerts.Length;
        while (true)
        {
            var pages = PaginateMetricAlerts(alerts: alerts,
                renderPage: (content, index) => Page(content: content, index: index, count: expectedCount));
            if (pages.Count == expectedCount)
            {
                return pages;
            }
            expectedCount = pages.Count;
        }
    }

    private List<string> PaginateMetricAlerts(IReadOnlyList<AlertmanagerAlert> alerts, Func<string, int, string> renderPage)
    {
        var pages = new List<string>();
        var current = string.Empty;
        foreach (var alert in alerts)
        {
            var block = BuildMetricAlertBlock(alert, int.MaxValue);
            var candidate = AppendMetricBlock(current, block);
            if (current.Length > 0 && NotificationMessage.GetTextLength(renderPage(candidate, pages.Count)) > NotificationMessage.MaxLength)
            {
                pages.Add(renderPage(current, pages.Count));
                current = string.Empty;
            }
            var overhead = NotificationMessage.GetTextLength(renderPage(block, pages.Count)) - NotificationMessage.GetTextLength(block);
            block = Fit(render: limit => BuildMetricAlertBlock(alert, limit), budget: NotificationMessage.MaxLength - overhead, html: true);
            current = AppendMetricBlock(current, block);
        }
        if (current.Length > 0)
        {
            pages.Add(renderPage(current, pages.Count));
        }
        return pages;
    }

    private static string AppendMetricBlock(string current, string block) => current.Length == 0
        ? block : $"{current}{Environment.NewLine}{Separator}{Environment.NewLine}{Environment.NewLine}{block}";

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

    private static int TextLength(string? value) => value?.EnumerateRunes().Count() ?? 0;

    private static string Truncate(string value, int budget)
    {
        if (TextLength(value) <= budget)
        {
            return value;
        }
        return budget <= 0 ? string.Empty : string.Concat(value.EnumerateRunes().Take(budget - 1)) + "…";
    }

    private static string HtmlTruncate(string value, int budget) => Html(Truncate(value, budget));

    private static string Fit(Func<int, string> render, int budget, bool html = false)
    {
        int Length(string value) => html ? NotificationMessage.GetTextLength(value) : TextLength(value);
        var full = render(int.MaxValue);
        if (Length(full) <= budget)
        {
            return full;
        }
        var minimum = 0;
        var maximum = budget;
        while (minimum < maximum)
        {
            var candidate = minimum + ((maximum - minimum + 1) / 2);
            if (Length(render(candidate)) <= budget)
            {
                minimum = candidate;
            }
            else
            {
                maximum = candidate - 1;
            }
        }
        return render(minimum);
    }
}
