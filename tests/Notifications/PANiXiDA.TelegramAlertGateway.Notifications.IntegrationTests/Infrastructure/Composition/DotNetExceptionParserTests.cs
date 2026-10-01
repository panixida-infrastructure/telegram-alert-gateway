using System.Globalization;
using System.Net.Sockets;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Infrastructure.Composition;

public sealed class DotNetExceptionParserTests
{
    [Theory(DisplayName = "Parse should preserve each exception and its stack when runtime formats nested exceptions")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_Should_PreserveEachExceptionAndItsStack_When_RuntimeFormatsNestedExceptions(string newline)
    {
        var inner = Capture(new TimeoutException("\nDatabase <timeout>\nSecond line \"details\" 🙂"));
        var middle = Capture(new IOException("Connection failed", inner));
        var outer = Capture(new InvalidOperationException("Save failed", middle));
        var text = Format(outer).ReplaceLineEndings(newline);

        var parsed = DotNetExceptionParser.Parse(text);

        parsed.ShouldNotBeNull();
        parsed.Select(item => item.Depth).ShouldBe([0, 1, 2]);
        parsed.Select(item => item.ClassName).ShouldBe(new[] { outer, middle, inner }.Select(item => item.GetType().FullName));
        parsed.Select(item => item.Message).ShouldBe([outer.Message, middle.Message, inner.Message]);
        parsed.Select(item => item.StackTrace).ShouldBe(new[] { outer, middle, inner }
            .Select(item => item.StackTrace.ShouldNotBeNull().ReplaceLineEndings("\n")));
    }

    [Theory(DisplayName = "Parse should retain sibling depths when aggregate exceptions contain nested branches")]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_Should_RetainSiblingDepths_When_AggregateExceptionsContainNestedBranches(bool captureStacks)
    {
        var leaf = new TimeoutException("Timeout");
        var first = new IOException("First branch", leaf);
        var second = new InvalidOperationException("Second branch", new ArgumentException("Invalid argument"));
        var aggregate = new AggregateException("Parallel operations failed", first, second);
        var outer = new InvalidOperationException("Request failed", aggregate);
        if (captureStacks)
        {
            Capture(leaf);
            Capture(first);
            Capture(second);
            Capture(aggregate);
            Capture(outer);
        }

        var parsed = DotNetExceptionParser.Parse(Format(outer));

        parsed.ShouldNotBeNull();
        parsed.Select(item => item.Depth).ShouldBe([0, 1, 2, 3, 2, 3]);
        parsed.Select(item => item.Message).ShouldBe(
        [
            outer.Message, aggregate.Message, first.Message, leaf.Message, second.Message, "Invalid argument"
        ]);
    }

    [Fact(DisplayName = "Parse should preserve native error code when inner socket exception includes a code")]
    public void Parse_Should_PreserveNativeErrorCode_When_InnerSocketExceptionIncludesACode()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var outer = new IOException("Connection failed", inner);

        var parsed = DotNetExceptionParser.Parse(Format(outer));

        parsed.ShouldNotBeNull();
        parsed[1].ClassName.ShouldBe(typeof(SocketException).FullName);
        var message = parsed[1].Message.ShouldNotBeNull();
        message.ShouldBe($"({inner.NativeErrorCode}): {inner.Message}");
        parsed[1].Depth.ShouldBe(1);
    }

    [Fact(DisplayName = "Parse should preserve generic type names when runtime formats constructed generic exceptions")]
    public void Parse_Should_PreserveGenericTypeNames_When_RuntimeFormatsConstructedGenericExceptions()
    {
        var inner = Capture(new GenericException<Dictionary<string, int[,]>>("Generic cause", new TimeoutException("Timeout")));
        var outer = Capture(new GenericException<string>("Generic wrapper", inner));

        var parsed = DotNetExceptionParser.Parse(Format(outer));

        parsed.ShouldNotBeNull();
        parsed.Select(item => item.Depth).ShouldBe([0, 1, 2]);
        parsed.Select(item => item.ClassName).ShouldBe([outer.GetType().ToString(), inner.GetType().ToString(), typeof(TimeoutException).FullName]);
        parsed.Select(item => item.Message).ShouldBe([outer.Message, inner.Message, "Timeout"]);
        parsed[0].StackTrace.ShouldBe(outer.StackTrace.ShouldNotBeNull().ReplaceLineEndings("\n"));
        parsed[1].StackTrace.ShouldBe(inner.StackTrace.ShouldNotBeNull().ReplaceLineEndings("\n"));
    }

    [Theory(DisplayName = "Parse should return no chain when input has no complete supported exception structure")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("at My.Service()\n  at My.Program()")]
    [InlineData("System.Exception: A single error\n   at My.Service()")]
    [InlineData("System.Exception: Text ---> not a nested exception")]
    [InlineData("System.Exception: Outer\n ---> System.Exception: Incomplete\n   at My.Service()")]
    [InlineData("System.Exception: Outer\n ---> invalid header text\n   --- End of inner exception stack trace ---")]
    [InlineData("System.Exception: Outer\n   --- End of inner exception stack trace ---")]
    [InlineData("System.Exception: Outer\n ---> (Inner Exception #1) System.Exception: Orphan<---")]
    [InlineData("System.Exception: Outer<---")]
    [InlineData("System.Exception: Outer\n ---> ")]
    public void Parse_Should_ReturnNoChain_When_InputHasNoCompleteSupportedExceptionStructure(string? input)
    {
        var parsed = DotNetExceptionParser.Parse(input);

        parsed.ShouldBeNull();
    }

    [Fact(DisplayName = "Parse should return no chain when nesting exceeds the processing limit")]
    public void Parse_Should_ReturnNoChain_When_NestingExceedsTheProcessingLimit()
    {
        var exception = new Exception("Cause");
        for (var index = 0; index < 40; index++)
        {
            exception = new Exception("Wrapper", exception);
        }

        var parsed = DotNetExceptionParser.Parse(Format(exception));

        parsed.ShouldBeNull();
    }

    [Fact(DisplayName = "Parse should return no chain when aggregate contains too many exceptions")]
    public void Parse_Should_ReturnNoChain_When_AggregateContainsTooManyExceptions()
    {
        var aggregate = new AggregateException(Enumerable.Range(0, 70).Select(index => new Exception($"Failure {index}")));

        var parsed = DotNetExceptionParser.Parse(Format(aggregate));

        parsed.ShouldBeNull();
    }

    [Fact(DisplayName = "Parse should return no chain when input exceeds the processing limit")]
    public void Parse_Should_ReturnNoChain_When_InputExceedsTheProcessingLimit()
    {
        var exception = new Exception(new string('x', 140_000), new Exception("Cause"));

        var parsed = DotNetExceptionParser.Parse(Format(exception));

        parsed.ShouldBeNull();
    }

    private sealed class GenericException<T>(string message, Exception innerException) : Exception(message, innerException);

    private static Exception Capture(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception captured)
        {
            return captured;
        }
    }

    private static string Format(Exception exception)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return exception.ToString();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
