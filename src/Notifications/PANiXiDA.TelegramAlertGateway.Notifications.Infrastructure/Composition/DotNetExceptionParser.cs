using System.Text;
using System.Text.RegularExpressions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

internal sealed record LogExceptionDetails(int Depth, string? ClassName, string? Message, string? StackTrace);

internal static partial class DotNetExceptionParser
{
    private const string InnerPrefix = " ---> ";
    private const string InnerEnd = "   --- End of inner exception stack trace ---";
    private const string AggregateEnd = "<---";
    private const int MaxDepth = 32;
    private const int MaxExceptions = 64;
    private const int MaxInputLength = 128 * 1024;

    public static IReadOnlyList<LogExceptionDetails>? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxInputLength)
        {
            return null;
        }

        var lines = value.ReplaceLineEndings("\n").Split('\n');
        var result = new List<LogExceptionDetails>();
        var position = 0;
        var parsed = TryReadException(
            lines: lines,
            position: ref position,
            depth: 0,
            terminator: null,
            result: result);
        return parsed && position == lines.Length && result.Count > 1 ? result : null;
    }

    private static bool TryReadException(
        string[] lines,
        ref int position,
        int depth,
        string? terminator,
        List<LogExceptionDetails> result)
    {
        if (depth > MaxDepth || result.Count >= MaxExceptions || position >= lines.Length)
        {
            return false;
        }

        var header = lines[position++];
        var closesInHeader = RemoveAggregateEnd(ref header);
        var match = ExceptionHeaderRegex().Match(header);
        if (!match.Success || (closesInHeader && terminator != AggregateEnd))
        {
            return false;
        }

        var className = match.Groups["type"].Value;
        var message = new StringBuilder(match.Groups["message"].Value);
        if (match.Groups["code"].Success)
        {
            message.Insert(0, match.Groups["code"].Value.TrimStart() + ": ");
        }
        var stack = new StringBuilder();
        var index = result.Count;
        result.Add(new LogExceptionDetails(Depth: depth, ClassName: className, Message: null, StackTrace: null));
        var parsed = closesInHeader || TryReadBody(
            lines: lines,
            position: ref position,
            depth: depth,
            terminator: terminator,
            result: result,
            message: message,
            stack: stack);
        result[index] = new LogExceptionDetails(
            Depth: depth,
            ClassName: className,
            Message: message.ToString().TrimEnd('\n'),
            StackTrace: stack.Length == 0 ? null : stack.ToString().TrimEnd('\n'));
        return parsed;
    }

    private static bool TryReadBody(
        string[] lines,
        ref int position,
        int depth,
        string? terminator,
        List<LogExceptionDetails> result,
        StringBuilder message,
        StringBuilder stack)
    {
        var hasInner = false;
        var readingStack = false;
        while (position < lines.Length)
        {
            var line = lines[position];
            if (TryConsumeInnerEnd(lines, ref position))
            {
                return terminator == InnerEnd;
            }

            if (line.StartsWith(InnerPrefix, StringComparison.Ordinal))
            {
                if (!TryReadInner(
                        lines: lines,
                        position: ref position,
                        depth: depth,
                        hasInner: hasInner,
                        result: result))
                {
                    return false;
                }

                hasInner = true;
                readingStack = true;
                continue;
            }

            position++;
            var closes = RemoveAggregateEnd(ref line);
            readingStack |= line.StartsWith("   at ", StringComparison.Ordinal);
            if (readingStack)
            {
                AppendLine(stack, line);
            }
            else
            {
                message.Append('\n').Append(line);
            }
            if (closes)
            {
                return terminator == AggregateEnd;
            }
        }

        return terminator is null;
    }

    private static bool TryConsumeInnerEnd(string[] lines, ref int position)
    {
        if (lines[position] == InnerEnd)
        {
            position++;
            return true;
        }

        if (lines[position] == InnerEnd + AggregateEnd)
        {
            lines[position] = AggregateEnd;
            return true;
        }

        return false;
    }

    private static bool TryReadInner(
        string[] lines,
        ref int position,
        int depth,
        bool hasInner,
        List<LogExceptionDetails> result)
    {
        var header = lines[position][InnerPrefix.Length..];
        var sibling = AggregateHeaderRegex().Match(header);
        if (sibling.Success != hasInner)
        {
            return false;
        }

        lines[position] = sibling.Success ? header[sibling.Length..] : header;
        return TryReadException(
            lines: lines,
            position: ref position,
            depth: depth + 1,
            terminator: sibling.Success ? AggregateEnd : InnerEnd,
            result: result);
    }

    private static bool RemoveAggregateEnd(ref string value)
    {
        if (!value.EndsWith(AggregateEnd, StringComparison.Ordinal))
        {
            return false;
        }

        value = value[..^AggregateEnd.Length];
        return true;
    }

    private static void AppendLine(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(value);
    }

    [GeneratedRegex(
        "^(?<type>[\\p{L}_][\\p{L}\\p{N}_.+`]*)(?:(?<code> \\([^\\r\\n)]*\\))?: (?<message>.*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionHeaderRegex();

    [GeneratedRegex("^\\(Inner Exception #[0-9]+\\) ", RegexOptions.CultureInvariant)]
    private static partial Regex AggregateHeaderRegex();
}
