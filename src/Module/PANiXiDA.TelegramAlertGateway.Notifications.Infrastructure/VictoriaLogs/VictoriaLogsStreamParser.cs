using System.Text;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal static partial class VictoriaLogsStreamParser
{
    public static IReadOnlyDictionary<string, string> Parse(string value)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var groups in StreamFieldRegex()
                     .Matches(value)
                     .Select(match => match.Groups))
        {
            fields.TryAdd(
                groups["name"].Value,
                Unescape(groups["value"].Value));
        }

        return fields;
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
                _ => $"\\{escapedCharacter}",
            };
            result.Append(replacement);
        }

        return result.ToString();
    }

    [GeneratedRegex(
        "(?<name>[^=,{}\\s]+)=\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex StreamFieldRegex();
}
