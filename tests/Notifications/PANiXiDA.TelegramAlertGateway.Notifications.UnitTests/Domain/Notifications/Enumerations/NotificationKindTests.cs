using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.Enumerations;

public sealed class NotificationKindTests
{
    [Fact(DisplayName = "Notification kind should expose all declared values when values are requested")]
    public void GetAll_Should_ReturnDeclaredValues_When_ValuesAreRequested()
    {
        var values = NotificationKind.GetAll();

        values.ShouldBe([NotificationKind.MetricAlert, NotificationKind.LogEvent]);
    }
}
