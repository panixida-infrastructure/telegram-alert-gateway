using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

public sealed partial class LogEventNormalizer(
    IOptions<VictoriaLogsOptions> options,
    TimeProvider timeProvider)
{
    private const string RedactedFieldValue = "[REDACTED]";
    private static readonly HashSet<string> ReservedFieldNames = new(
        [
            "_msg",
            "_seq",
            "_stream",
            "_stream_id",
            "_tenant_id",
            "_time",
            "alert_owner",
            "app",
            "body",
            "container",
            "deployment.environment",
            "exception.stack_trace",
            "exception.stacktrace",
            "exception.type",
            "exception_type",
            "k8s.container.name",
            "k8s.namespace.name",
            "klog_level",
            "kubernetes.container_name",
            "kubernetes.namespace_name",
            "level",
            "log.level",
            "LogLevel",
            "message",
            "msg",
            "namespace",
            "owner",
            "service",
            "service.name",
            "service_name",
            "severity",
            "severity_text",
            "stack_trace",
            "StackTrace",
            "t",
            "time",
            "timestamp",
            "trace.id",
            "trace_id"
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly string[] SensitiveFieldNameFragments =
    [
        "api_key",
        "apikey",
        "authorization",
        "connection_string",
        "connectionstring",
        "cookie",
        "credential",
        "password",
        "passwd",
        "private_key",
        "privatekey",
        "secret",
        "token"
    ];

    private readonly VictoriaLogsOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public IReadOnlyList<LogEvent> Normalize(
        IReadOnlyList<IReadOnlyDictionary<string, string>> records)
    {
        return records
            .Select(TryNormalize)
            .Where(item => item is not null)
            .Select(item => item!)
            .GroupBy(item => item.Fingerprint, StringComparer.Ordinal)
            .Select(group =>
            {
                var sourceGroup = group
                    .GroupBy(CreateSourceIdentity, StringComparer.Ordinal)
                    .OrderByDescending(item => item.Count())
                    .ThenByDescending(item => item.Any(logEvent =>
                        !string.IsNullOrWhiteSpace(logEvent.TraceId)))
                    .ThenByDescending(item => item.Any(logEvent =>
                        !string.IsNullOrWhiteSpace(logEvent.ExceptionType)))
                    .First();
                var preferred = sourceGroup
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.TraceId))
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.ExceptionType))
                    .ThenByDescending(item => item.Message.Length)
                    .First();

                return preferred with { Occurrences = sourceGroup.Count() };
            })
            .OrderBy(item => item.Service, StringComparer.Ordinal)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal)
            .ToArray();
    }

    private LogEvent? TryNormalize(IReadOnlyDictionary<string, string> fields)
    {
        var rawMessage = GetValue(fields, "_msg", "body", "message", "Message");
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return null;
        }

        var parsedRecord = StructuredLogRecordParser.Parse(rawMessage);
        var message = GetValue(fields, "message", "Message", "msg")
            ?? parsedRecord.Message
            ?? rawMessage;

        var severity = GetValue(
            fields,
            "severity_text",
            "severity",
            "level",
            "log.level",
            "SeverityText",
            "LogLevel");
        if (!IsError(severity))
        {
            return null;
        }

        var service = GetValue(fields, "service.name", "service_name", "service", "app")
            ?? GetValue(fields, "k8s.container.name", "container")
            ?? "unknown-service";
        if (_options.ExcludedServices.Any(excluded =>
                service.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var namespaceName = GetValue(
                fields,
                "k8s.namespace.name",
                "kubernetes.namespace_name",
                "namespace")
            ?? GetValue(fields, "deployment.environment")
            ?? string.Empty;
        var container = GetValue(
                fields,
                "k8s.container.name",
                "kubernetes.container_name",
                "container")
            ?? string.Empty;
        var exceptionType = GetValue(
            fields,
            "exception.type",
            "exception_type",
            "ExceptionType");
        var stackTrace = GetValue(
            fields,
            "exception.stacktrace",
            "exception.stack_trace",
            "stack_trace",
            "StackTrace");
        var traceId = GetValue(fields, "trace_id", "trace.id", "TraceId");
        var owner = GetValue(fields, "alert_owner", "owner");
        var timestampValue = GetValue(fields, "_time", "timestamp", "Timestamp");
        var timestamp = DateTimeOffset.TryParse(
            input: timestampValue,
            formatProvider: CultureInfo.InvariantCulture,
            styles: DateTimeStyles.AssumeUniversal,
            result: out var parsedTimestamp)
            ? parsedTimestamp
            : _timeProvider.GetUtcNow();
        var fingerprint = CreateFingerprint(
            service: service,
            namespaceName: namespaceName,
            exceptionType: exceptionType,
            message: rawMessage);

        return new LogEvent(
            Timestamp: timestamp,
            Service: service,
            Namespace: namespaceName,
            Container: container,
            Owner: owner,
            Severity: string.IsNullOrWhiteSpace(severity) ? "error" : severity,
            Message: message,
            ExceptionType: exceptionType,
            StackTrace: stackTrace,
            TraceId: traceId,
            Fields: CreateStructuredFields(fields, parsedRecord.Fields),
            Fingerprint: fingerprint,
            Occurrences: 1,
            StreamId: GetValue(fields, "_stream_id"));
    }

    private static Dictionary<string, string> CreateStructuredFields(
        IReadOnlyDictionary<string, string> sourceFields,
        IReadOnlyDictionary<string, string> parsedFields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddStructuredFields(result, sourceFields);
        AddStructuredFields(result, parsedFields);
        return result;
    }

    private static void AddStructuredFields(
        IDictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var field in source)
        {
            if (string.IsNullOrWhiteSpace(field.Key)
                || string.IsNullOrWhiteSpace(field.Value)
                || ReservedFieldNames.Contains(field.Key))
            {
                continue;
            }

            var value = IsSensitiveFieldName(field.Key)
                ? RedactedFieldValue
                : field.Value.Trim();
            destination.TryAdd(field.Key, value);
        }
    }

    private static bool IsSensitiveFieldName(string fieldName)
    {
        return SensitiveFieldNameFragments.Any(fragment =>
            fieldName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsError(string? severity)
    {
        return !string.IsNullOrWhiteSpace(severity)
               && ErrorSeverityRegex().IsMatch(severity);
    }

    private static string CreateSourceIdentity(LogEvent logEvent)
    {
        return string.Join(
            separator: '\u001F',
            values:
            [
                logEvent.Service,
                logEvent.Namespace,
                logEvent.Container
            ]);
    }

    private static string CreateFingerprint(
        string service,
        string namespaceName,
        string? exceptionType,
        string message)
    {
        var owner = service;
        if (service.Contains("tactical-heroes", StringComparison.OrdinalIgnoreCase)
            || namespaceName.Contains("tactical-heroes", StringComparison.OrdinalIgnoreCase))
        {
            owner = "tactical-heroes";
        }
        else if (service.Contains("dotnet-template", StringComparison.OrdinalIgnoreCase)
                 || namespaceName.Contains("dotnet-template", StringComparison.OrdinalIgnoreCase))
        {
            owner = "dotnet-template";
        }
        var normalizedMessage = NormalizeMessageForFingerprint(message);
        var source = $"{owner}|{exceptionType}|{normalizedMessage}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static string NormalizeMessageForFingerprint(string message)
    {
        var fingerprintMessage = TryCreateKubernetesApiFailureFingerprint(message) ?? message;

        return DynamicValueRegex()
            .Replace(Ipv4AddressRegex().Replace(fingerprintMessage, "#"), "#")
            .Trim();
    }

    private static string? TryCreateKubernetesApiFailureFingerprint(string message)
    {
        if (!message.AsSpan().TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var error = GetJsonString(document.RootElement, "error");
            if (string.IsNullOrWhiteSpace(error) || !IsKubernetesApiRequest(error))
            {
                return null;
            }

            var failure = GetKubernetesApiFailure(error);
            var summary = GetJsonString(document.RootElement, "msg", "message");

            return failure is null || string.IsNullOrWhiteSpace(summary)
                ? null
                : $"{summary}|kubernetes-api|{failure}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsKubernetesApiRequest(string error)
    {
        return error.Contains("https://10.96.0.1", StringComparison.OrdinalIgnoreCase)
               || error.Contains("local-k8s-", StringComparison.OrdinalIgnoreCase)
               || error.Contains("kubernetes.default.svc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetKubernetesApiFailure(string error)
    {
        if (error.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return "connection-refused";
        }

        if (error.Contains("TLS handshake timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "tls-handshake-timeout";
        }

        if (error.Contains("connection reset by peer", StringComparison.OrdinalIgnoreCase))
        {
            return "connection-reset";
        }

        if (error.Contains("context deadline exceeded", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Client.Timeout exceeded", StringComparison.OrdinalIgnoreCase)
            || error.Contains("i/o timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "request-timeout";
        }

        return error.Contains("client connection lost", StringComparison.OrdinalIgnoreCase)
            ? "connection-lost"
            : null;
    }

    private static string? GetJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.GetString()))
            {
                return property.GetString();
            }
        }

        return null;
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

    [GeneratedRegex("^(error|fatal|critical)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorSeverityRegex();

    [GeneratedRegex("(?i)(?:[0-9a-f]{8}-[0-9a-f-]{27,}|\\b\\d{2,}\\b|0x[0-9a-f]+|\\d{4}-\\d{2}-\\d{2}T[^\\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicValueRegex();

    [GeneratedRegex("\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4AddressRegex();
}
