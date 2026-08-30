using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Composition;

public sealed class TelegramNotificationComposerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Metric alerts are paginated without dropping alerts")]
    public void ComposeMetricAlerts_Should_Paginate_Without_Dropping_Alerts()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alerts = Enumerable.Range(1, 10)
            .Select(index => new AlertmanagerAlert(
                "firing",
                new Dictionary<string, string>
                {
                    ["alertname"] = $"alert-{index}",
                    ["severity"] = "warning",
                    ["alert_owner"] = "tactical-heroes"
                },
                new Dictionary<string, string>
                {
                    ["summary"] = $"summary-{index}",
                    ["description"] = new string((char)('a' + index), 700)
                },
                DateTimeOffset.UtcNow,
                null,
                "https://grafana.panixida.ru",
                $"fingerprint-{index}"))
            .ToArray();

        var notifications = composer.ComposeMetricAlerts(
            "firing",
            "https://alertmanager.example",
            alerts);
        var rendered = string.Join('\n', notifications.Select(item => item.Message));

        notifications.Count.ShouldBeGreaterThan(1);
        notifications.ShouldAllBe(item => item.Topic == "tactical-heroes");
        notifications.ShouldAllBe(item => item.Message.Length <= Notification.MaxMessageLength);
        rendered.ShouldNotContain("---");
        foreach (var alert in alerts)
        {
            rendered.ShouldContain(alert.Labels["alertname"]);
        }
    }

    [Fact(DisplayName = "Different owners are routed to exactly one topic each")]
    public void ComposeMetricAlerts_Should_Route_Each_Alert_To_One_Owner_Topic()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alerts = new[]
        {
            CreateAlert("tactical-heroes", "first"),
            CreateAlert("dotnet-template", "second")
        };

        var notifications = composer.ComposeMetricAlerts("firing", string.Empty, alerts);

        notifications.Select(item => item.Topic)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["dotnet-template", "tactical-heroes"]);
    }

    [Fact(DisplayName = "A later occurrence of the same alert gets a new notification key")]
    public void ComposeMetricAlerts_Should_Not_Suppress_A_Later_Alert_Occurrence()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var first = CreateAlert("tactical-heroes", "same-fingerprint");
        var later = first with { StartsAt = first.StartsAt.AddHours(1) };

        var firstNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [first])
            .ShouldHaveSingleItem();
        var laterNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [later])
            .ShouldHaveSingleItem();

        laterNotification.Key.ShouldNotBe(firstNotification.Key);
    }

    private static AlertmanagerAlert CreateAlert(string owner, string fingerprint)
    {
        return new AlertmanagerAlert(
            "firing",
            new Dictionary<string, string>
            {
                ["alertname"] = fingerprint,
                ["alert_owner"] = owner
            },
            new Dictionary<string, string> { ["summary"] = fingerprint },
            DateTimeOffset.UtcNow,
            null,
            string.Empty,
            fingerprint);
    }
}
