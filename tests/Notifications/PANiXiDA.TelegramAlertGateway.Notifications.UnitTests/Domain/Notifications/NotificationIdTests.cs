using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications;

public sealed class NotificationIdTests
{
    [Fact(DisplayName = "Notification id should create a version 7 identifier when invoked")]
    public void New_Should_CreateVersion7Guid_When_Invoked()
    {
        var id = NotificationId.New();

        id.Value.ShouldNotBe(Guid.Empty);
        id.Value.Version.ShouldBe(7);
    }

    [Fact(DisplayName = "Notification id should preserve a valid value when value is valid")]
    public void Create_Should_ReturnId_When_ValueIsValid()
    {
        var value = Guid.CreateVersion7();

        var result = NotificationId.Create(value: value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Fact(DisplayName = "Notification id should reject an empty value when value is empty")]
    public void Create_Should_ReturnFailure_When_ValueIsEmpty()
    {
        var result = NotificationId.Create(value: Guid.Empty);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Notification id should return its value when converted to string")]
    public void ToString_Should_ReturnValue_When_ConvertedToString()
    {
        var value = Guid.CreateVersion7();
        var id = NotificationId.Create(value: value).Value;

        var result = id.ToString();

        result.ShouldBe(value.ToString());
    }
}
