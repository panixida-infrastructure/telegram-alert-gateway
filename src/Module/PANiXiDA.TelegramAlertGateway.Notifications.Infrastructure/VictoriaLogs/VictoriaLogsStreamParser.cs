using System.Text;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal static partial class VictoriaLogsStreamParser
{
    public static IReadOnlyDictionary<string, string> Parse(string value)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in StreamFieldRegex().Matches(value))
        {
            fields.TryAdd(
                match.Groups["name"].Value,
                Unescape(match.Groups["value"].Value));
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
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                result.Append(character);
                continue;
            }

            var escapedCharacter = value[++index];
            switch (escapedCharacter)
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case '\\':
                case '"':
                    result.Append(escapedCharacter);
                    break;
                default:
                    result.Append('\\').Append(escapedCharacter);
                    break;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(
        "(?<name>[^=,{}\\s]+)=\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex StreamFieldRegex();
}
