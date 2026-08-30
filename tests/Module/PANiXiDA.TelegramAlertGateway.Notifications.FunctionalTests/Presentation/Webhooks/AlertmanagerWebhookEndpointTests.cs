using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation.Webhooks;

public sealed class AlertmanagerWebhookEndpointTests(FunctionalTestFixture fixture)
    : FunctionalTestBase(fixture)
{
    private const string Endpoint = "/api/v1/webhooks/alertmanager";

    [Fact(DisplayName = "Alertmanager webhook rejects requests without bearer token")]
    public async Task Webhook_Should_Return_Unauthorized_When_Token_Is_Missing()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        using var response = await Client.PostAsJsonAsync(
            Endpoint,
            CreateRequest(),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Alertmanager webhook persists one notification for retried payload")]
    public async Task Webhook_Should_Persist_One_Notification_When_Payload_Is_Retried()
    {
        using var firstResponse = await SendAuthorizedAsync();
        using var retryResponse = await SendAuthorizedAsync();

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retryResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await using var scope = Fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var notificationsCount = await dbContext.Notifications.CountAsync(
            TestContext.Current.CancellationToken);
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
