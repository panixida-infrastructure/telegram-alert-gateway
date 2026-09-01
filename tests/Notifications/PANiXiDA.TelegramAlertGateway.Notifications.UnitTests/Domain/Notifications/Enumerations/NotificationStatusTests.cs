using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.Enumerations;

public sealed class NotificationStatusTests
{
    [Fact(DisplayName = "Notification status should expose all declared values when values are requested")]
    public void GetAll_Should_ReturnDeclaredValues_When_ValuesAreRequested()
    {
        var values = NotificationStatus.GetAll();

        values.ShouldBe(
        [
            NotificationStatus.Pending,
            NotificationStatus.Processing,
            NotificationStatus.Sent,
            NotificationStatus.Failed
        ]);
    }
}
