using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.ValueObjects;

public sealed class NotificationMessageTests
{
    [Theory(DisplayName = "Create should enforce parsed text limit when html contains unicode and links")]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void Create_Should_EnforceParsedTextLimit_When_HtmlContainsUnicodeAndLinks(int count, bool valid)
    {
        var value = "<a href=\"https://grafana.example/" + new string('x', 5000) + "\"><b>"
                    + string.Concat(Enumerable.Repeat("&#128578;", count)) + "</b></a>";

        var result = NotificationMessage.Create(value);

        result.IsSuccess.ShouldBe(valid);
        if (valid)
        {
            result.Value.Value.ShouldBe(value);
        }
    }

    [Theory(DisplayName = "Get text length should decode after stripping markup when text contains escaped tags")]
    [InlineData("<pre>&lt;b&gt;Я&amp;🙂&lt;/b&gt;</pre>", 10)]
    [InlineData("<b></b>", 0)]
    public void GetTextLength_Should_DecodeAfterStrippingMarkup_When_TextContainsEscapedTags(string value, int expected)
    {
        var length = NotificationMessage.GetTextLength(value);

        length.ShouldBe(expected);
    }

    [Fact(DisplayName = "Notification message should preserve content when value is valid")]
    public void Create_Should_ReturnMessage_When_ValueIsValid()
    {
        var result = NotificationMessage.Create(value: "error");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("error");
    }

    [Fact(DisplayName = "Notification message should reject empty content when value is empty")]
    public void Create_Should_ReturnFailure_When_ValueIsEmpty()
    {
        var result = NotificationMessage.Create(value: string.Empty);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Notification message should return its value when converted to string")]
    public void ToString_Should_ReturnValue_When_ConvertedToString()
    {
        var message = NotificationMessage.Create(value: "error").Value;

        var result = message.ToString();

        result.ShouldBe("error");
    }
}
