using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation.Features.Webhooks.Alertmanager;

public sealed class AlertmanagerWebhookEndpointTests(FunctionalTestFixture fixture)
    : FunctionalTestBase(fixture)
{
    private const string Endpoint = "/api/v1/webhooks/alertmanager";

    [Fact(DisplayName = "Webhook should return unauthorized when token is missing")]
    public async Task Webhook_Should_ReturnUnauthorized_When_TokenIsMissing()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        using var response = await Client.PostAsJsonAsync(
            Endpoint,
            CreateRequest(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Webhook should persist one notification when payload is retried")]
    public async Task Webhook_Should_PersistOneNotification_When_PayloadIsRetried()
    {
        using var firstResponse = await SendAuthorizedAsync();
        using var retryResponse = await SendAuthorizedAsync();

        await using var scope = Fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var notificationsCount = await dbContext.Set<Notification>().CountAsync(
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retryResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        notificationsCount.ShouldBe(1);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(CreateRequest())
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-webhook-token");

        return await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static object CreateRequest()
    {
        return new
        {
            status = "firing",
            externalUrl = "https://alertmanager.example",
            alerts = new[]
            {
                new
                {
                    status = "firing",
                    labels = new Dictionary<string, string>
                    {
                        ["alertname"] = "ApiUnavailable",
                        ["severity"] = "critical",
                        ["alert_owner"] = "tactical-heroes"
                    },
                    annotations = new Dictionary<string, string>
                    {
                        ["summary"] = "Tactical Heroes API is unavailable."
                    },
                    startsAt = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero),
                    endsAt = (DateTimeOffset?)null,
                    generatorUrl = "https://grafana.panixida.ru",
                    fingerprint = "stable-fingerprint"
                }
            }
        };
    }
}
