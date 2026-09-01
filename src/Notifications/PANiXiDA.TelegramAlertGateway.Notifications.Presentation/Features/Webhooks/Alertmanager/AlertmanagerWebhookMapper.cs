using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;

using Riok.Mapperly.Abstractions;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Features.Webhooks.Alertmanager;

[Mapper]
internal static partial class AlertmanagerWebhookMapper
{
    internal static partial QueueMetricAlertsCommand ToCommand(
        AlertmanagerWebhookRequest request,
        DateTimeOffset receivedAtUtc);
}
