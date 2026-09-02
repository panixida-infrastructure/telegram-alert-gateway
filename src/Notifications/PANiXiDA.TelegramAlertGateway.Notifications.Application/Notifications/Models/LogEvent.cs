namespace PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    string Service,
    string Namespace,
    string Container,
    string? Owner,
    string Severity,
    string Message,
    string? ExceptionType,
    string? StackTrace,
    string? TraceId,
    IReadOnlyDictionary<string, string> Fields,
    string Fingerprint,
    int Occurrences,
    string? StreamId = null);
