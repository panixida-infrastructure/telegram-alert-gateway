using System.Globalization;
using System.Text.Json;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal static class LogGroupQuery
{
    internal const string GroupIdField = "alert.group.id";
    internal const string RecordUidsField = "alert.group.record_uids";
    internal const string MembershipService = "telegram-alert-gateway";
    private const int MaxInlineQueryEncodedLength = 700;

    internal static (string Query, string? GroupId)? Create(DateTimeOffset windowStartUtc, LogEvent logEvent)
    {
        if (logEvent.Occurrences <= 1 || logEvent.RecordUids is not { Count: > 0 } recordUids)
        {
            return null;
        }

        var values = JsonSerializer.Serialize(recordUids);
        var query = $"log.record.uid:in({values[1..^1]})";
        if (Uri.EscapeDataString(JsonSerializer.Serialize(query)).Length <= MaxInlineQueryEncodedLength)
        {
            return (query, null);
        }

        var groupId = NotificationKeyFactory.Create($"log-group|{windowStartUtc.UtcTicks}|{logEvent.Fingerprint}|{values}");
        // Grafana zoom must not narrow the membership lookup along with the displayed logs.
        var membershipDate = windowStartUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return ($"log.record.uid:in(options(ignore_global_time_filter=true) _time:{membershipDate} "
                + $"service.name:={JsonSerializer.Serialize(MembershipService)} "
                + $"{GroupIdField}:={JsonSerializer.Serialize(groupId)} "
                + $"| unroll {RecordUidsField} | fields {RecordUidsField})", groupId);
    }
}
