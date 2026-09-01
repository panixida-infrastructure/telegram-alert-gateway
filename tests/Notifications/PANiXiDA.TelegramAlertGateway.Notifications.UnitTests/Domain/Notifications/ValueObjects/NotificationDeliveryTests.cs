using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Domain.Notifications.ValueObjects;

public sealed class NotificationDeliveryTests
{
    [Fact(DisplayName = "Notification delivery should start pending when created")]
    public void Start_Should_ReturnPendingDelivery_When_Created()
    {
        var now = DateTimeOffset.UtcNow;

        var delivery = NotificationDelivery.Start(createdAtUtc: now);

        delivery.Status.ShouldBe(NotificationStatus.Pending);
        delivery.CreatedAtUtc.ShouldBe(now);
        delivery.AvailableAtUtc.ShouldBe(now);
        delivery.Attempts.ShouldBe(0);
    }

    [Fact(DisplayName = "Notification delivery should increment attempts when processing starts")]
    public void MarkProcessing_Should_IncrementAttempts_When_ProcessingStarts()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = NotificationDelivery.Start(createdAtUtc: now);

        var result = delivery.MarkProcessing(nowUtc: now.AddMinutes(1));

        result.Status.ShouldBe(NotificationStatus.Processing);
        result.Attempts.ShouldBe(1);
        result.AvailableAtUtc.ShouldBe(now.AddMinutes(1));
    }

    [Fact(DisplayName = "Notification delivery should record sent time when delivery succeeds")]
    public void MarkSent_Should_RecordSentTime_When_DeliverySucceeds()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = NotificationDelivery.Start(createdAtUtc: now)
            .MarkProcessing(nowUtc: now);

        var result = delivery.MarkSent(nowUtc: now.AddMinutes(1));

        result.Status.ShouldBe(NotificationStatus.Sent);
        result.SentAtUtc.ShouldBe(now.AddMinutes(1));
    }

    [Fact(DisplayName = "Notification delivery should retain the error when delivery is rescheduled")]
    public void Reschedule_Should_RetainError_When_DeliveryIsRescheduled()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = NotificationDelivery.Start(createdAtUtc: now)
            .MarkProcessing(nowUtc: now);

        var result = delivery.Reschedule(
            availableAtUtc: now.AddMinutes(1),
            error: "network error");

        result.Status.ShouldBe(NotificationStatus.Pending);
        result.LastError.ShouldBe("network error");
    }

    [Fact(DisplayName = "Notification delivery should truncate the error when error is too long")]
    public void MarkFailed_Should_TruncateError_When_ErrorIsTooLong()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = NotificationDelivery.Start(createdAtUtc: now);

        var result = delivery.MarkFailed(
            nowUtc: now,
            error: new string('e', NotificationDelivery.MaxLength + 1));

        result.Status.ShouldBe(NotificationStatus.Failed);
        result.LastError!.Length.ShouldBe(NotificationDelivery.MaxLength);
    }
}
