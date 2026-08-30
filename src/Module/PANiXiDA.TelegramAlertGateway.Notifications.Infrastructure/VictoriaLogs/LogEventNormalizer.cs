using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

public sealed partial class LogEventNormalizer(IOptions<VictoriaLogsOptions> options)
{
    private readonly VictoriaLogsOptions _options = options.Value;

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
                var preferred = group
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.TraceId))
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.ExceptionType))
                    .ThenByDescending(item => item.Message.Length)
                    .First();

                var occurrences = group
                    .GroupBy(CreateSourceIdentity, StringComparer.Ordinal)
                    .Max(sourceGroup => sourceGroup.Count());

                return preferred with { Occurrences = occurrences };
            })
            .OrderBy(item => item.Service, StringComparer.Ordinal)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal)
            .ToArray();
    }

    private LogEvent? TryNormalize(IReadOnlyDictionary<string, string> fields)
    {
        var message = GetValue(fields, "_msg", "body", "message", "Message");
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

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
        var timestampValue = GetValue(fields, "_time", "timestamp", "Timestamp");
        var timestamp = DateTimeOffset.TryParse(timestampValue, out var parsedTimestamp)
            ? parsedTimestamp
            : DateTimeOffset.UtcNow;
        var fingerprint = CreateFingerprint(service, namespaceName, exceptionType, message);

        return new LogEvent(
            timestamp,
            service,
            namespaceName,
            container,
            string.IsNullOrWhiteSpace(severity) ? "error" : severity,
            message,
            exceptionType,
            stackTrace,
            traceId,
            fingerprint,
            1);
    }

    private static bool IsError(string? severity)
    {
        return !string.IsNullOrWhiteSpace(severity)
               && ErrorSeverityRegex().IsMatch(severity);
    }

    private static string CreateSourceIdentity(LogEvent logEvent)
    {
        return string.Join(
            '\u001F',
            logEvent.Service,
            logEvent.Namespace,
            logEvent.Container);
    }

    private static string CreateFingerprint(
        string service,
        string namespaceName,
        string? exceptionType,
        string message)
    {
        var owner = service.Contains("tactical-heroes", StringComparison.OrdinalIgnoreCase)
            || namespaceName.Contains("tactical-heroes", StringComparison.OrdinalIgnoreCase)
                ? "tactical-heroes"
                : service.Contains("dotnet-template", StringComparison.OrdinalIgnoreCase)
                  || namespaceName.Contains("dotnet-template", StringComparison.OrdinalIgnoreCase)
                    ? "dotnet-template"
                    : service;
        var normalizedMessage = DynamicValueRegex()
            .Replace(message, "#")
            .Trim();
        var source = $"{owner}|{exceptionType}|{normalizedMessage}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
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
}
