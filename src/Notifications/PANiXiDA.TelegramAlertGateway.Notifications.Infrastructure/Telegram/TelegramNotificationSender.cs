using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.Telegram;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telemetry;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telegram;

internal sealed class TelegramNotificationSender(
    TelegramClientFactory clientFactory,
    IOptions<TelegramOptions> options,
    ILogger<TelegramNotificationSender> logger) : ITelegramNotificationSender
{
    private readonly TelegramOptions _options = options.Value;

    public async Task SendAsync(
        string topic,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_options.Topics.TryGetValue(topic, out var threadId))
        {
            throw new InvalidOperationException($"Telegram topic '{topic}' is not configured.");
        }

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            if (!string.IsNullOrWhiteSpace(_options.ProxyUrl))
            {
                try
                {
                    await SendAsync(
                        client: clientFactory.CreateProxiedClient(),
                        threadId: threadId,
                        message: message,
                        cancellationToken: cancellationToken);
                    var vpnTags = new TagList { { "route", "vpn" } };
                    GatewayTelemetry.SentNotifications.Add(1, vpnTags);
                    return;
                }
                catch (RequestException exception) when (CanUseDirectFallback(exception))
                {
                    GatewayTelemetry.DirectFallbacks.Add(1);
                    logger.LogWarning(
                        exception,
                        "Telegram VPN route failed. Falling back to direct egress.");
                }
            }

            await SendAsync(
                client: clientFactory.CreateDirectClient(),
                threadId: threadId,
                message: message,
                cancellationToken: cancellationToken);
            var directTags = new TagList { { "route", "direct" } };
            GatewayTelemetry.SentNotifications.Add(1, directTags);
        }
        catch
        {
            GatewayTelemetry.FailedNotifications.Add(1);
            throw;
        }
        finally
        {
            GatewayTelemetry.DeliveryDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
        }
    }

    private async Task SendAsync(
        ITelegramBotClient client,
        int threadId,
        string message,
        CancellationToken cancellationToken)
    {
        await client.SendMessage(
            chatId: new ChatId(_options.ChatId),
            text: message,
            parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            messageThreadId: threadId,
            cancellationToken: cancellationToken);
    }

    private static bool CanUseDirectFallback(RequestException exception)
    {
        return exception.HttpStatusCode is null
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }
}
