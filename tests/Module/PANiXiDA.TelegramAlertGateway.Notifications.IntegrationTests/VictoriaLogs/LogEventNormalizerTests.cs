using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class LogEventNormalizerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Direct OTLP and container copies of one error are grouped")]
    public void Normalize_Should_Group_Direct_And_Container_Copies_Of_One_Error()
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
                ["service.name"] = "api",
                ["k8s.namespace.name"] = "tactical-heroes-production",
                ["k8s.container.name"] = "api"
            }
        };

        var events = normalizer.Normalize(records);

        var logEvent = events.ShouldHaveSingleItem();
        logEvent.Occurrences.ShouldBe(2);
        logEvent.TraceId.ShouldBe("trace-1");
    }
}
