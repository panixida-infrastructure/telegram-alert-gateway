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

    [Fact(DisplayName = "Structured log owner is preserved for topic routing")]
    public void Normalize_Should_Preserve_Explicit_Log_Owner()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string>
            {
                ["_time"] = "2026-08-30T10:00:05Z",
                ["_msg"] = "Telegram alert gateway VictoriaLogs smoke test",
                ["severity_text"] = "Error",
                ["service.name"] = "log-smoke",
                ["alert_owner"] = "tests"
            }
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Owner.ShouldBe("tests");
    }

    [Fact(DisplayName = "Metrics server node timeouts are aggregated across dynamic node addresses")]
    public void Normalize_Should_Aggregate_Metrics_Server_Timeouts_Across_Node_Addresses()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            CreateMetricsServerRecord(
                "2026-09-01T17:46:08Z",
                "E0901 17:46:08.284073 1 scraper.go:147] \"Failed to scrape node, timeout to access kubelet\" err=\"Get \\\"https://192.168.10.7:10250/metrics/resource\\\": context deadline exceeded\" node=\"worker-192.168.10.7\" timeout=\"10s\""),
            CreateMetricsServerRecord(
                "2026-09-01T17:46:18Z",
                "E0901 17:46:18.288441 1 scraper.go:147] \"Failed to scrape node, timeout to access kubelet\" err=\"Get \\\"https://192.168.10.12:10250/metrics/resource\\\": context deadline exceeded\" node=\"worker-192.168.10.12\" timeout=\"10s\""),
            CreateMetricsServerRecord(
                "2026-09-01T17:46:28Z",
                "E0901 17:46:28.288441 1 scraper.go:147] \"Failed to scrape node, timeout to access kubelet\" err=\"Get \\\"https://192.168.10.12:10250/metrics/resource\\\": context deadline exceeded\" node=\"worker-192.168.10.12\" timeout=\"10s\"")
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Occurrences.ShouldBe(3);
    }

    [Fact(DisplayName = "Kubernetes API transport failures are aggregated across watched resources")]
    public void Normalize_Should_Aggregate_Kubernetes_Api_Failures_Across_Resources()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            CreateArgoCdRecord(
                "2026-09-01T17:47:01Z",
                """{"error":"failed to list *v1.ConfigMap: Get \"https://10.96.0.1:443/api/v1/namespaces/argocd/configmaps?resourceVersion=42419091\": net/http: TLS handshake timeout","level":"error","msg":"Failed to watch","time":"2026-09-01T17:47:01Z"}"""),
            CreateArgoCdRecord(
                "2026-09-01T17:47:11Z",
                """{"error":"failed to list *v1.Role: Get \"https://10.96.0.1:443/apis/rbac.authorization.k8s.io/v1/roles?resourceVersion=42419156\": net/http: TLS handshake timeout","level":"error","msg":"Failed to watch","time":"2026-09-01T17:47:11Z"}""")
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Occurrences.ShouldBe(2);
    }

    private static Dictionary<string, string> CreateRecord(
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

    private static Dictionary<string, string> CreateMetricsServerRecord(
        string timestamp,
        string message)
    {
        return new Dictionary<string, string>
        {
            ["_time"] = timestamp,
            ["_msg"] = message,
            ["LogLevel"] = "Error",
            ["service.name"] = "metrics-server",
            ["k8s.namespace.name"] = "kube-system",
            ["k8s.container.name"] = "metrics-server"
        };
    }

    private static Dictionary<string, string> CreateArgoCdRecord(
        string timestamp,
        string message)
    {
        return new Dictionary<string, string>
        {
            ["_time"] = timestamp,
            ["_msg"] = message,
            ["severity_text"] = "Error",
            ["service.name"] = "application-controller",
            ["k8s.namespace.name"] = "argocd",
            ["k8s.container.name"] = "application-controller"
        };
    }
}
