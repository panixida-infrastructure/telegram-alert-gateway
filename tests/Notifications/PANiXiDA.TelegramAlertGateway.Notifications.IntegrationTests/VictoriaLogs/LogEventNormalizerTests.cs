using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class LogEventNormalizerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(DisplayName = "Normalize should deduplicate direct and container copies of one error when ingestion paths differ")]
    public void Normalize_Should_DeduplicateDirectAndContainerCopiesOfOneError_When_IngestionPathsDiffer()
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

    [Fact(DisplayName = "Normalize should count repeated application occurrences when ingestion paths differ")]
    public void Normalize_Should_CountRepeatedApplicationOccurrences_When_IngestionPathsDiffer()
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

    [Fact(DisplayName = "Normalize should ignore informational error words when severity is informational")]
    public void Normalize_Should_IgnoreInformationalErrorWords_When_SeverityIsInformational()
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

    [Fact(DisplayName = "Normalize should preserve explicit log owner when owner is structured")]
    public void Normalize_Should_PreserveExplicitLogOwner_When_OwnerIsStructured()
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

    [Fact(DisplayName = "Normalize should aggregate metrics server timeouts across node addresses when node addresses differ")]
    public void Normalize_Should_AggregateMetricsServerTimeoutsAcrossNodeAddresses_When_NodeAddressesDiffer()
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

    [Fact(DisplayName = "Normalize should aggregate Kubernetes API failures across resources when watched resources differ")]
    public void Normalize_Should_AggregateKubernetesApiFailuresAcrossResources_When_WatchedResourcesDiffer()
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

    [Fact(DisplayName = "Normalize should keep different Kubernetes API failures separate when failure kinds differ")]
    public void Normalize_Should_KeepDifferentKubernetesApiFailuresSeparate_When_FailureKindsDiffer()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var failures = new[]
        {
            "dial tcp 10.96.0.1:443: connect: connection refused",
            "net/http: TLS handshake timeout",
            "read tcp 10.0.1.73:34994->10.96.0.1:443: connection reset by peer",
            "context deadline exceeded",
            "http2: client connection lost"
        };
        var records = failures
            .Select((failure, index) => CreateArgoCdRecord(
                $"2026-09-01T17:47:{index + 20:D2}Z",
                JsonSerializer.Serialize(new
                {
                    error = $"Get https://10.96.0.1:443/api/v1/pods: {failure}",
                    level = "error",
                    msg = "Watch failed"
                })))
            .ToArray();

        var events = normalizer.Normalize(records);

        events.Count.ShouldBe(failures.Length);
    }

    [Fact(DisplayName = "Normalize should preserve generic fields when log uses logfmt")]
    public void Normalize_Should_PreserveGenericFields_When_LogUsesLogfmt()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string>
            {
                ["_time"] = "2026-09-02T17:08:33Z",
                ["_msg"] = "logger=infra.usagestats.collector t=2026-09-02T17:08:33Z level=error msg=\"Failed to read data sources\" error=\"plugin not found\"",
                ["_stream"] = "{service.name=\"grafana\"}",
                ["_stream_id"] = "0000007b000001c850d9950ea6196b1a4812081265faa1c7",
                ["severity_text"] = "Error",
                ["service.name"] = "grafana",
                ["k8s.namespace.name"] = "observability",
                ["k8s.container.name"] = "grafana",
                ["k8s.pod.name"] = "grafana-0",
                ["UserId"] = "42",
                ["access_token"] = "should-not-be-rendered"
            }
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Message.ShouldBe("Failed to read data sources");
        logEvent.Fields["logger"].ShouldBe("infra.usagestats.collector");
        logEvent.Fields["error"].ShouldBe("plugin not found");
        logEvent.Fields["k8s.pod.name"].ShouldBe("grafana-0");
        logEvent.Fields["UserId"].ShouldBe("42");
        logEvent.Fields["access_token"].ShouldBe("[REDACTED]");
        logEvent.StreamId.ShouldBe("0000007b000001c850d9950ea6196b1a4812081265faa1c7");
        logEvent.Fields.ContainsKey("_stream").ShouldBeFalse();
        logEvent.Fields.ContainsKey("_stream_id").ShouldBeFalse();
        logEvent.Fields.ContainsKey("severity_text").ShouldBeFalse();
    }

    [Fact(DisplayName = "Normalize should parse generic fields when log uses klog")]
    public void Normalize_Should_ParseGenericFields_When_LogUsesKlog()
    {
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string>
            {
                ["_time"] = "2026-09-02T16:39:26Z",
                ["_msg"] = "E0902 16:39:26.838894 1 reflector.go:205] \"Failed to watch\" err=\"failed to list *v1.Node: connection refused\" logger=\"UnhandledError\" reflector=\"k8s.io/client-go/tools/cache/reflector.go:290\" type=\"*v1.Node\"",
                ["severity_text"] = "Error",
                ["service.name"] = "otel-collector",
                ["k8s.namespace.name"] = "observability",
                ["k8s.container.name"] = "otel-collector"
            }
        };

        var logEvent = normalizer.Normalize(records).ShouldHaveSingleItem();

        logEvent.Message.ShouldBe("Failed to watch");
        logEvent.Fields["klog.source"].ShouldBe("reflector.go:205");
        logEvent.Fields["err"].ShouldContain("connection refused");
        logEvent.Fields["logger"].ShouldBe("UnhandledError");
        logEvent.Fields["reflector"].ShouldBe("k8s.io/client-go/tools/cache/reflector.go:290");
        logEvent.Fields["type"].ShouldBe("*v1.Node");
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
