using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications;

public sealed class NotificationTests
{
    [Fact(DisplayName = "Notification requires lower kebab case topic")]
    public void Create_Should_Fail_When_Topic_Is_Not_Lower_Kebab_Case()
    {
        var result = Notification.Create(
            new string('a', 64),
            "TacticalHeroes",
            NotificationKind.LogEvent,
            "error",
            DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Notification tracks delivery lifecycle")]
    public void DeliveryLifecycle_Should_Update_Status_And_Attempts()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            new string('a', 64),
            "tactical-heroes",
            NotificationKind.LogEvent,
            "error",
            now).Value;

        notification.MarkProcessing(now);
        notification.Reschedule(now.AddMinutes(1), "network error");
        notification.MarkProcessing(now.AddMinutes(1));
        notification.MarkSent(now.AddMinutes(2));

        notification.Status.ShouldBe(NotificationStatus.Sent);
        notification.Attempts.ShouldBe(2);
        notification.SentAtUtc.ShouldBe(now.AddMinutes(2));
        notification.LastError.ShouldBeNull();
    }
}
