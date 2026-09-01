using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.ValueObjects;

public sealed class NotificationKeyTests
{
    [Fact(DisplayName = "Notification key should preserve a SHA 256 value when value is valid")]
    public void Create_Should_ReturnKey_When_ValueIsValid()
    {
        var value = new string('a', NotificationKey.MaxLength);

        var result = NotificationKey.Create(value: value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Fact(DisplayName = "Notification key should reject malformed input when value is invalid")]
    public void Create_Should_ReturnFailure_When_ValueIsInvalid()
    {
        var result = NotificationKey.Create(value: "invalid");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Notification key should return its value when converted to string")]
    public void ToString_Should_ReturnValue_When_ConvertedToString()
    {
        var value = new string('a', NotificationKey.MaxLength);
        var key = NotificationKey.Create(value: value).Value;

        var result = key.ToString();

        result.ShouldBe(value);
    }
}
