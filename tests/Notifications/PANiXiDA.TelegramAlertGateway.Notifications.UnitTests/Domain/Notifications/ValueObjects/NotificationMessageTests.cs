using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.ValueObjects;

public sealed class NotificationMessageTests
{
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
