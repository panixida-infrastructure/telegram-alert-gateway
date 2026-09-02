using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal sealed record StructuredLogRecord(
    string? Message,
    IReadOnlyDictionary<string, string> Fields);

internal static partial class StructuredLogRecordParser
{
    public static StructuredLogRecord Parse(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryParseJson(
                body: body,
                fields: fields,
                message: out var message))
        {
            return new StructuredLogRecord(message, fields);
        }

        if (TryParseKlog(
                body: body,
                fields: fields,
                message: out message))
        {
            return new StructuredLogRecord(message, fields);
        }

        ParseKeyValueFields(body, fields);

        return new StructuredLogRecord(
            GetValue(fields, "message", "msg"),
            fields);
    }

    private static bool TryParseJson(
        string body,
        IDictionary<string, string> fields,
        out string? message)
    {
        message = null;
        if (!body.AsSpan().TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.GetRawText()
                };
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fields.TryAdd(property.Name, value);
                }
            }

            message = GetValue(fields, "message", "msg");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseKlog(
        string body,
        IDictionary<string, string> fields,
        out string? message)
    {
        var match = KlogRecordRegex().Match(body);
        if (!match.Success)
        {
            message = null;
            return false;
        }

        message = Unescape(match.Groups["message"].Value);
        fields.TryAdd("klog.source", match.Groups["source"].Value);
        ParseKeyValueFields(match.Groups["fields"].Value, fields);
        return true;
    }

    private static void ParseKeyValueFields(
        string value,
        IDictionary<string, string> fields)
    {
        foreach (Match match in KeyValueFieldRegex().Matches(value))
        {
            var fieldValue = match.Groups["quoted"].Success
                ? Unescape(match.Groups["quoted"].Value)
                : match.Groups["unquoted"].Value;
            if (!string.IsNullOrWhiteSpace(fieldValue))
            {
                fields.TryAdd(match.Groups["name"].Value, fieldValue);
            }
        }
    }

    private static string? GetValue(
        IDictionary<string, string> values,
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

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index++];
            if (character != '\\' || index == value.Length)
            {
                result.Append(character);
                continue;
            }

            var escapedCharacter = value[index++];
            var replacement = escapedCharacter switch
            {
                'n' => "\n",
                'r' => "\r",
                't' => "\t",
                '\\' or '"' => escapedCharacter.ToString(),
                _ => $"\\{escapedCharacter}"
            };
            result.Append(replacement);
        }

        return result.ToString();
    }

    [GeneratedRegex(
        "^[IWEF][0-9]{4}\\s+[0-9:.]+\\s+[0-9]+\\s+(?<source>[^]]+)]\\s+\"(?<message>(?:\\\\.|[^\"\\\\])*)\"(?<fields>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KlogRecordRegex();

    [GeneratedRegex(
        "(?:^|\\s)(?<name>[A-Za-z_][A-Za-z0-9_.-]*)=(?:\"(?<quoted>(?:\\\\.|[^\"\\\\])*)\"|(?<unquoted>[^\\s]+))",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueFieldRegex();
}
