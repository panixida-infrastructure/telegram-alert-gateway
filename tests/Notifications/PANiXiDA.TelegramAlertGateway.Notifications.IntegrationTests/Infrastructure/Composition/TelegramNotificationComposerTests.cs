using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Infrastructure.Composition;

public sealed class TelegramNotificationComposerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Compose metric alerts should paginate without dropping alerts when message limit is reached")]
    public void ComposeMetricAlerts_Should_PaginateWithoutDroppingAlerts_When_MessageLimitIsReached()
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
            alerts,
            DateTimeOffset.UtcNow);
        var rendered = string.Join('\n', notifications.Select(item => item.Message));

        notifications.Count.ShouldBeGreaterThan(1);
        notifications.ShouldAllBe(item => item.Topic == "tactical-heroes");
        notifications.ShouldAllBe(item => item.Message.Length <= NotificationMessage.MaxLength);
        rendered.ShouldNotContain("---");
        foreach (var alert in alerts)
        {
            rendered.ShouldContain(alert.Labels["alertname"]);
        }
    }

    [Fact(DisplayName = "Compose metric alerts should route each alert to one owner topic when owners differ")]
    public void ComposeMetricAlerts_Should_RouteEachAlertToOneOwnerTopic_When_OwnersDiffer()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alerts = new[]
        {
            CreateAlert("tactical-heroes", "first"),
            CreateAlert("dotnet-template", "second")
        };

        var notifications = composer.ComposeMetricAlerts(
            "firing",
            string.Empty,
            alerts,
            DateTimeOffset.UtcNow);

        notifications.Select(item => item.Topic)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["dotnet-template", "tactical-heroes"]);
    }

    [Theory(DisplayName = "Compose metric alerts should prioritize explicit owner when labels match another route")]
    [InlineData("core-platform", "grafana")]
    [InlineData("tests", "telegram-alert-gateway-smoke")]
    public void ComposeMetricAlerts_Should_PrioritizeExplicitOwner_When_LabelsMatchAnotherRoute(
        string owner,
        string conflictingService)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert(owner, $"{owner}-explicit-owner");
        var labels = alert.Labels.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        labels["service"] = conflictingService;

        var notification = composer
            .ComposeMetricAlerts(
                "firing",
                string.Empty,
                [alert with { Labels = labels }],
                DateTimeOffset.UtcNow)
            .ShouldHaveSingleItem();

        notification.Topic.ShouldBe(owner);
    }

    [Fact(DisplayName = "Compose log event should route Timeweb CSI to core platform when service belongs to timeweb csi")]
    public void ComposeLogEvent_Should_RouteTimewebCsiToCorePlatform_When_ServiceBelongsToTimewebCsi()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var timestamp = new DateTimeOffset(2026, 8, 31, 7, 37, 8, TimeSpan.Zero);
        var logEvent = new LogEvent(
            timestamp,
            "external-provisioner",
            "csi-driver-timeweb-cloud",
            "external-provisioner",
            null,
            "error",
            "Failed to watch PersistentVolume",
            null,
            null,
            null,
            "timeweb-csi-fingerprint",
            1);

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Topic.ShouldBe("core-platform");
    }

    [Theory(DisplayName = "Compose log event should route Kubernetes platform service to core platform when service belongs to kubernetes")]
    [InlineData("metrics-server", "kube-system", "metrics-server")]
    [InlineData("external-secrets", "external-secrets", "external-secrets")]
    [InlineData("cert-controller", "external-secrets", "cert-controller")]
    public void ComposeLogEvent_Should_RouteKubernetesPlatformServiceToCorePlatform_When_ServiceBelongsToKubernetes(
        string service,
        string namespaceName,
        string container)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var timestamp = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero);
        var logEvent = new LogEvent(
            timestamp,
            service,
            namespaceName,
            container,
            null,
            "error",
            "Kubernetes platform component failed",
            null,
            null,
            null,
            $"{service}-fingerprint",
            1);

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Topic.ShouldBe("core-platform");
    }

    [Fact(DisplayName = "Compose log event should use unclassified fallback when owner is unknown")]
    public void ComposeLogEvent_Should_UseUnclassifiedFallback_When_OwnerIsUnknown()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var timestamp = new DateTimeOffset(2026, 8, 31, 7, 37, 8, TimeSpan.Zero);
        var logEvent = new LogEvent(
            timestamp,
            "unknown-service",
            "unknown-namespace",
            "unknown-container",
            null,
            "error",
            "Unknown error",
            null,
            null,
            null,
            "unknown-fingerprint",
            1);

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Topic.ShouldBe("unclassified");
    }

    [Fact(DisplayName = "Compose log event should prioritize explicit test owner when owner is provided")]
    public void ComposeLogEvent_Should_PrioritizeExplicitTestOwner_When_OwnerIsProvided()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var timestamp = new DateTimeOffset(2026, 8, 31, 7, 37, 8, TimeSpan.Zero);
        var logEvent = new LogEvent(
            timestamp,
            "log-smoke",
            "alert-gateway-smoke",
            "log-smoke",
            "tests",
            "error",
            "Telegram alert gateway VictoriaLogs smoke test",
            null,
            null,
            null,
            "log-smoke-fingerprint",
            1);

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Topic.ShouldBe("tests");
    }

    [Fact(DisplayName = "Compose log event should render configured log window when event is composed")]
    public void ComposeLogEvent_Should_RenderConfiguredLogWindow_When_EventIsComposed()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var windowStart = new DateTimeOffset(2026, 9, 1, 17, 46, 0, TimeSpan.Zero);
        var logEvent = new LogEvent(
            windowStart.AddSeconds(8),
            "metrics-server",
            "kube-system",
            "metrics-server",
            null,
            "error",
            "Failed to scrape node, timeout to access kubelet",
            null,
            null,
            null,
            "metrics-server-timeout",
            5);

        var notification = composer.ComposeLogEvent(windowStart, logEvent);

        notification.Message.ShouldContain(
            "repeated <b>5 times</b> in a 1-minute log window");
    }

    [Fact(DisplayName = "Compose log event should create new key for next window when same error repeats")]
    public void ComposeLogEvent_Should_CreateNewKeyForNextWindow_When_SameErrorRepeats()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var firstWindow = new DateTimeOffset(2026, 9, 1, 17, 46, 0, TimeSpan.Zero);
        var logEvent = new LogEvent(
            firstWindow.AddSeconds(8),
            "metrics-server",
            "kube-system",
            "metrics-server",
            null,
            "error",
            "Failed to scrape node, timeout to access kubelet",
            null,
            null,
            null,
            "metrics-server-timeout",
            5);

        var first = composer.ComposeLogEvent(firstWindow, logEvent);
        var next = composer.ComposeLogEvent(firstWindow.AddMinutes(1), logEvent);

        next.Key.ShouldNotBe(first.Key);
    }

    [Fact(DisplayName = "Compose metric alerts should not suppress later alert occurrence when alert starts later")]
    public void ComposeMetricAlerts_Should_NotSuppressLaterAlertOccurrence_When_AlertStartsLater()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var first = CreateAlert("tactical-heroes", "same-fingerprint");
        var later = first with { StartsAt = first.StartsAt.AddHours(1) };
        var receivedAtUtc = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

        var firstNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [first], receivedAtUtc)
            .ShouldHaveSingleItem();
        var laterNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [later], receivedAtUtc)
            .ShouldHaveSingleItem();

        laterNotification.Key.ShouldNotBe(firstNotification.Key);
    }

    [Fact(DisplayName = "Compose metric alerts should deduplicate retries without suppressing scheduled repeats when alerts share fingerprint")]
    public void ComposeMetricAlerts_Should_DeduplicateRetriesWithoutSuppressingScheduledRepeats_When_AlertsShareFingerprint()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert("tactical-heroes", "stable-fingerprint");
        var firstDelivery = new DateTimeOffset(2026, 8, 30, 10, 1, 0, TimeSpan.Zero);

        var firstNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [alert], firstDelivery)
            .ShouldHaveSingleItem();
        var retryNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [alert], firstDelivery.AddMinutes(1))
            .ShouldHaveSingleItem();
        var scheduledRepeatNotification = composer
            .ComposeMetricAlerts("firing", string.Empty, [alert], firstDelivery.AddHours(4))
            .ShouldHaveSingleItem();

        retryNotification.Key.ShouldBe(firstNotification.Key);
        scheduledRepeatNotification.Key.ShouldNotBe(firstNotification.Key);
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
