using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class LogEventNormalizerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Direct OTLP and container copies of one error are deduplicated")]
    public void Normalize_Should_Deduplicate_Direct_And_Container_Copies_Of_One_Error()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string>
            {
                ["_time"] = "2026-08-30T10:00:05Z",
                ["_msg"] = "Failed to load hero 12345",
                ["severity_text"] = "Error",
                ["service.name"] = "tactical-heroes-api-production",
                ["trace_id"] = "trace-1"
            },
            new Dictionary<string, string>
            {
                ["_time"] = "2026-08-30T10:00:05Z",
                ["_msg"] = "Failed to load hero 67890",
                ["LogLevel"] = "Error",
                ["service.name"] = "api",
                ["k8s.namespace.name"] = "tactical-heroes-production",
                ["k8s.container.name"] = "api"
            }
        };

        var events = normalizer.Normalize(records);

        var logEvent = events.ShouldHaveSingleItem();
        logEvent.Occurrences.ShouldBe(1);
        logEvent.TraceId.ShouldBe("trace-1");
    }

    [Fact(DisplayName = "Repeated copies are counted by application occurrence, not ingestion path")]
    public void Normalize_Should_Count_Repeated_Application_Occurrences()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            CreateRecord("2026-08-30T10:00:05Z", "Failed to load hero 12345", "direct"),
            CreateRecord("2026-08-30T10:00:05Z", "Failed to load hero 12345", "container"),
            CreateRecord("2026-08-30T10:00:15Z", "Failed to load hero 67890", "direct"),
            CreateRecord("2026-08-30T10:00:15Z", "Failed to load hero 67890", "container")
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Occurrences.ShouldBe(2);
    }

    [Fact(DisplayName = "Informational messages containing error words are ignored")]
    public void Normalize_Should_Ignore_Informational_Error_Words()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string>
            {
                ["_time"] = "2026-08-30T10:00:05Z",
                ["_msg"] = "Processed window with 250 error groups.",
                ["LogLevel"] = "Information",
                ["service.name"] = "telegram-alert-gateway"
            },
            new Dictionary<string, string>
            {
                ["_time"] = "2026-08-30T10:00:06Z",
                ["_msg"] = "Controller retry failed but recovered.",
                ["service.name"] = "controller"
            }
        };

        normalizer.Normalize(records).ShouldBeEmpty();
    }

    private static IReadOnlyDictionary<string, string> CreateRecord(
        string timestamp,
        string message,
        string source)
    {
        return source == "direct"
            ? new Dictionary<string, string>
            {
                ["_time"] = timestamp,
                ["_msg"] = message,
                ["severity_text"] = "Error",
                ["service.name"] = "tactical-heroes-api-production",
                ["trace_id"] = $"trace-{timestamp}"
            }
            : new Dictionary<string, string>
            {
                ["_time"] = timestamp,
                ["_msg"] = message,
                ["LogLevel"] = "Error",
                ["service.name"] = "api",
                ["k8s.namespace.name"] = "tactical-heroes-production",
                ["k8s.container.name"] = "api"
            };
    }
}
