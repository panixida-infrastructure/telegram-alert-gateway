using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications;

public sealed class NotificationTests
{
    [Fact(DisplayName = "Notification should reject an invalid topic when topic is invalid")]
    public void Create_Should_ReturnFailure_When_TopicIsInvalid()
    {
        var result = Notification.Create(
            key: new string('a', 64),
            topic: "TacticalHeroes",
            kind: NotificationKind.LogEvent,
            message: "error",
            createdAtUtc: DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "Notification should increment attempts when processing starts")]
    public void MarkProcessing_Should_IncrementAttempts_When_ProcessingStarts()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(createdAtUtc: now);

        notification.MarkProcessing(now: now.AddMinutes(1));

        notification.Delivery.Status.ShouldBe(NotificationStatus.Processing);
        notification.Delivery.Attempts.ShouldBe(1);
        notification.Delivery.AvailableAtUtc.ShouldBe(now.AddMinutes(1));
    }

    [Fact(DisplayName = "Notification should record delivery time when delivery succeeds")]
    public void MarkSent_Should_RecordSentTime_When_DeliverySucceeds()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(createdAtUtc: now);
        notification.MarkProcessing(now: now.AddMinutes(1));

        notification.MarkSent(now: now.AddMinutes(2));

        notification.Delivery.Status.ShouldBe(NotificationStatus.Sent);
        notification.Delivery.SentAtUtc.ShouldBe(now.AddMinutes(2));
        notification.Delivery.LastError.ShouldBeNull();
    }

    [Fact(DisplayName = "Notification should preserve attempts when delivery is rescheduled")]
    public void Reschedule_Should_PreserveAttempts_When_DeliveryIsRescheduled()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(createdAtUtc: now);
        notification.MarkProcessing(now: now.AddMinutes(1));

        notification.Reschedule(
            availableAtUtc: now.AddMinutes(2),
            error: "network error");

        notification.Delivery.Status.ShouldBe(NotificationStatus.Pending);
        notification.Delivery.Attempts.ShouldBe(1);
        notification.Delivery.LastError.ShouldBe("network error");
    }

    [Fact(DisplayName = "Notification should retain the final error when delivery fails")]
    public void MarkFailed_Should_RetainError_When_DeliveryFails()
    {
        var now = DateTimeOffset.UtcNow;
        var notification = CreateNotification(createdAtUtc: now);
        notification.MarkProcessing(now: now.AddMinutes(1));

        notification.MarkFailed(
            now: now.AddMinutes(2),
            error: "delivery failed");

        notification.Delivery.Status.ShouldBe(NotificationStatus.Failed);
        notification.Delivery.LastError.ShouldBe("delivery failed");
    }

    private static Notification CreateNotification(DateTimeOffset createdAtUtc)
    {
        return Notification.Create(
            key: new string('a', 64),
            topic: "tactical-heroes",
            kind: NotificationKind.LogEvent,
            message: "error",
            createdAtUtc: createdAtUtc).Value;
    }
}
