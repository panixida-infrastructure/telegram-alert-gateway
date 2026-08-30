using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueMetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Configuration;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Features.Webhooks.Alertmanager;

internal sealed class AlertmanagerWebhookEndpoint : IEndpoint<WebhooksEndpoints>
{
    public string Route { get; } = "/alertmanager";
    public string Name { get; } = "ReceiveAlertmanagerWebhook";
    public string Summary { get; } = "Queue Alertmanager notifications";

    public void Map(EndpointMapBuilder builder)
    {
        builder.MapPost(HandleAsync)
            .Produces<QueueNotificationsResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> HandleAsync(
        AlertmanagerWebhookRequest request,
        HttpContext httpContext,
        IOptions<WebhookOptions> options,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(httpContext, options.Value.Token))
        {
            return TypedResults.Unauthorized();
        }

        var alerts = request.Alerts.Select(alert => new AlertmanagerAlert(
            alert.Status,
            alert.Labels,
            alert.Annotations,
            alert.StartsAt,
            alert.EndsAt,
            alert.GeneratorUrl,
            alert.Fingerprint)).ToArray();
        var result = await mediator.SendAsync(
            new QueueMetricAlertsCommand(
                request.Status,
                request.ExternalUrl,
                alerts,
                DateTimeOffset.UtcNow),
            cancellationToken);

        if (result.IsFailure)
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Accepted(
            uri: (string?)null,
            value: new QueueNotificationsResponse(result.Value));
    }

    private static bool IsAuthorized(HttpContext context, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedToken = authorization[bearerPrefix.Length..];
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

internal sealed record QueueNotificationsResponse(int Queued);
