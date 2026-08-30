using System.Diagnostics.Metrics;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telemetry;

public static class GatewayTelemetry
{
    public const string MeterName = "PANiXiDA.TelegramAlertGateway";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> SentNotifications = Meter.CreateCounter<long>(
        "telegram_alert_gateway.notifications.sent");

    public static readonly Counter<long> FailedNotifications = Meter.CreateCounter<long>(
        "telegram_alert_gateway.notifications.failed");

    public static readonly Counter<long> DeletedNotifications = Meter.CreateCounter<long>(
        "telegram_alert_gateway.notifications.deleted");

    public static readonly Counter<long> DirectFallbacks = Meter.CreateCounter<long>(
        "telegram_alert_gateway.telegram.direct_fallbacks");

    public static readonly Histogram<double> DeliveryDuration = Meter.CreateHistogram<double>(
        "telegram_alert_gateway.notification.delivery.duration",
        "s");
}
