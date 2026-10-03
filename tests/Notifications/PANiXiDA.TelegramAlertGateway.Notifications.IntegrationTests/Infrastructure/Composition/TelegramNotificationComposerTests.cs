using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.Infrastructure.Composition;

public sealed class TelegramNotificationComposerTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory(DisplayName = "Compose metric alerts should preserve contextual links when public urls are configured")]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposeMetricAlerts_Should_PreserveContextualLinks_When_PublicUrlsAreConfigured(bool longDescription)
    {
        using var scope = Fixture.CreateScope();
        const string alertmanagerUrl = "https://grafana.example/alerting/groups?alertmanager=Alertmanager";
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions { AlertmanagerUrl = alertmanagerUrl }),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions()));
        var dashboardUrl = "https://grafana.example/d/application-telemetry/application-telemetry"
                           + "?orgId=1&from=1790990000000&to=now&var-service="
                           + Uri.EscapeDataString("api <&>/" + new string('x', 640))
                           + "&viewPanel=panel-15";
        var alert = CreateAlert("tests", "links") with
        {
            Annotations = new Dictionary<string, string>
            {
                ["summary"] = longDescription ? new string('s', 600) : "Slow route",
                ["description"] = longDescription ? new string('d', 900) : "Route <api> & p95 > 2s",
                ["dashboard_url"] = dashboardUrl
            }
        };
        if (longDescription)
        {
            alert = alert with
            {
                Labels = new Dictionary<string, string>(alert.Labels)
                {
                    ["alertname"] = new string('a', 180),
                    ["service_name"] = new string('s', 180),
                    ["severity"] = new string('w', 40),
                    ["instance"] = new string('i', 500)
                }
            };
        }

        var message = composer.ComposeMetricAlerts(
            "firing", "http://alertmanager-0:9093", [alert], DateTimeOffset.UtcNow).Single().Message;
        var decoded = WebUtility.HtmlDecode(message);

        decoded.ShouldContain($"href=\"{dashboardUrl}\">Grafana</a>");
        decoded.ShouldContain($"href=\"{alertmanagerUrl}\">Alertmanager</a>");
        decoded.ShouldContain($"{Environment.NewLine}{Environment.NewLine}🔗 <a href=\"{dashboardUrl}\">");
        decoded.ShouldContain($">Grafana</a>{Environment.NewLine}🔗 <a href=\"{alertmanagerUrl}\">");
        decoded.ShouldNotContain("Open details");
        decoded.ShouldNotContain("alertmanager-0");
        message.ShouldNotContain("<pre>");
        message.ShouldContain("&amp;");
        ReadMessageText(message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        if (longDescription)
        {
            message.ShouldContain(new string('d', 900));
            message.ShouldContain("🖥 Instance: ");
        }
        else
        {
            message.ShouldContain($"📖 Route &lt;api&gt; &amp; p95 &gt; 2s{Environment.NewLine}");
        }
    }

    [Theory(DisplayName = "Compose metric alerts should identify target when endpoint or instance labels are available")]
    [InlineData("https://demo:secret@example.com/health?token=secret", "collector:8888", "", "🌐 <a href=\"https://example.com/\">example.com:443</a>\n🖥 Instance: collector:8888")]
    [InlineData("http://example.com:8080/health", "", "", "🌐 <a href=\"http://example.com:8080/\">example.com:8080</a>")]
    [InlineData("https://[2001:db8::1]:8443/health", "", "", "🌐 <a href=\"https://[2001:db8::1]:8443/\">[2001:db8::1]:8443</a>")]
    [InlineData("", "example.com:9100", "worker-1", "🖥 Instance: <a href=\"http://example.com:9100/\">example.com:9100</a>")]
    [InlineData("https://example.com", "worker-1:8080", "", "🌐 <a href=\"https://example.com/\">example.com:443</a>\n🖥 Instance: <a href=\"http://worker-1:8080/\">worker-1:8080</a>")]
    [InlineData("https://example.com", "worker-1:8081", "", "🌐 <a href=\"https://example.com/\">example.com:443</a>\n🖥 Instance: <a href=\"http://worker-1:8081/\">worker-1:8081</a>")]
    [InlineData("", "https://demo:secret@worker-1:9443/path?token=secret", "", "🖥 Instance: <a href=\"https://worker-1:9443/\">worker-1:9443</a>")]
    [InlineData("", "worker-1:443", "", "🖥 Instance: <a href=\"https://worker-1/\">worker-1:443</a>")]
    [InlineData("", "db:5432", "", "🖥 Instance: db:5432")]
    [InlineData("", "9100", "", "🖥 Instance: 9100")]
    [InlineData("", "worker:9100/path", "", "🖥 Instance: worker:9100/path")]
    [InlineData("file:///private", "worker<&>:9100", "", "🖥 Instance: worker&lt;&amp;&gt;:9100")]
    [InlineData("", "", "worker-1", "🖥 Instance: worker-1")]
    [InlineData("", "", "", "")]
    public void ComposeMetricAlerts_Should_IdentifyTarget_When_EndpointOrInstanceLabelsAreAvailable(
        string httpUrl,
        string instance,
        string serviceInstanceId,
        string expectedTarget)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert("tests", "endpoint") with
        {
            Labels = new Dictionary<string, string>
            {
                ["alert_owner"] = "tests",
                ["http_url"] = httpUrl,
                ["instance"] = instance,
                ["service_instance_id"] = serviceInstanceId
            }
        };

        var message = composer.ComposeMetricAlerts("firing", "", [alert], DateTimeOffset.UtcNow).Single().Message;
        var targetLines = message.Split(Environment.NewLine)
            .Where(line => line.StartsWith("🌐 ", StringComparison.Ordinal) || line.StartsWith("🖥 ", StringComparison.Ordinal))
            .ToArray();

        if (expectedTarget.Length == 0)
        {
            targetLines.ShouldBeEmpty();
        }
        else
        {
            targetLines.ShouldBe(expectedTarget.Split('\n'));
        }
        message.ShouldNotContain("secret");
        message.ShouldNotContain("file:///private");
    }

    [Fact(DisplayName = "Compose metric alerts should preserve target link when url exceeds old budget")]
    public void ComposeMetricAlerts_Should_PreserveTargetLink_When_UrlExceedsOldBudget()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert("tests", "long-target");
        alert = alert with
        {
            Labels = new Dictionary<string, string>(alert.Labels)
            {
                ["http_url"] = "https://" + string.Concat(Enumerable.Repeat("longlabel.", 20)) + "example.com/health"
            }
        };

        var message = composer.ComposeMetricAlerts("firing", "", [alert], DateTimeOffset.UtcNow).Single().Message;

        ReadMessageText(message).ShouldContain("🌐 longlabel.");
        message.ShouldContain("<a ");
        ReadMessageText(message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Fact(DisplayName = "Compose metric alerts should count each status when group contains firing and resolved alerts")]
    public void ComposeMetricAlerts_Should_CountEachStatus_When_GroupContainsFiringAndResolvedAlerts()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alerts = new[] { CreateAlert("tests", "first"), CreateAlert("tests", "second") with { Status = "resolved" } };

        var message = composer.ComposeMetricAlerts("firing", "", alerts, DateTimeOffset.UtcNow).Single().Message;

        message.ShouldContain("📊 Firing: <b>1</b> | Resolved: <b>1</b>");
    }

    [Fact(DisplayName = "Compose metric alerts should use overview when dashboard is missing")]
    public void ComposeMetricAlerts_Should_UseOverview_When_DashboardIsMissing()
    {
        using var scope = Fixture.CreateScope();
        const string dashboardUrl = "https://grafana.example/d/overview/overview?from=now-1h&to=now";
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions { GrafanaDashboardUrl = dashboardUrl }),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions()));
        var alert = CreateAlert("tests", "overview") with
        {
            GeneratorUrl = "http://vmalert:8880/internal"
        };

        var message = composer.ComposeMetricAlerts("firing", "", [alert], DateTimeOffset.UtcNow).Single().Message;

        WebUtility.HtmlDecode(message).ShouldContain($"href=\"{dashboardUrl}\">Grafana</a>");
        message.ShouldNotContain("vmalert");
    }

    [Theory(DisplayName = "Compose metric alerts should omit unusable link when url is invalid")]
    [InlineData("")]
    [InlineData("file:///C:/private")]
    [InlineData("javascript:alert(1)")]
    public void ComposeMetricAlerts_Should_OmitUnusableLink_When_UrlIsInvalid(string url)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert("tests", "invalid-link") with
        {
            Annotations = new Dictionary<string, string> { ["dashboard_url"] = url }
        };

        var message = composer.ComposeMetricAlerts("firing", "https://alertmanager.example", [alert], DateTimeOffset.UtcNow).Single().Message;

        message.ShouldNotContain(">Grafana</a>");
        message.ShouldContain($"{Environment.NewLine}{Environment.NewLine}🔗 <a href=\"https://alertmanager.example\">Alertmanager</a>");
        message.ShouldContain("invalid-link");
        ReadMessageText(message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Theory(DisplayName = "Metric alert options should validate urls when configured")]
    [InlineData("", true)]
    [InlineData("https://grafana.example/alerting/groups?alertmanager=Alertmanager", true)]
    [InlineData("http://localhost:3000/d/overview", true)]
    [InlineData("file:///C:/private", false)]
    [InlineData("/relative", false)]
    public void MetricAlertsOptions_Should_ValidateUrls_When_Configured(string url, bool valid)
    {
        var validator = new MetricAlertsOptionsValidator();

        validator.Validate(null, new MetricAlertsOptions { AlertmanagerUrl = url }).Succeeded.ShouldBe(valid);
        validator.Validate(null, new MetricAlertsOptions { GrafanaDashboardUrl = url }).Succeeded.ShouldBe(valid);
        validator.Validate(null, new MetricAlertsOptions { AlertmanagerUrl = "https://grafana.example/" + new string('a', 351) })
            .Succeeded.ShouldBeTrue();
    }

    [Fact(DisplayName = "Metric alert options should accept long HTML when url is valid")]
    public void MetricAlertsOptions_Should_AcceptLongHtml_When_UrlIsValid()
    {
        var url = "https://grafana.example/?" + string.Concat(Enumerable.Repeat("a=1&", 20)) + new string('x', 190);
        var validator = new MetricAlertsOptionsValidator();

        var result = validator.Validate(null, new MetricAlertsOptions { AlertmanagerUrl = url });

        url.Length.ShouldBeLessThanOrEqualTo(300);
        result.Succeeded.ShouldBeTrue();
        validator.Validate(null, new MetricAlertsOptions { GrafanaDashboardUrl = url }).Succeeded.ShouldBeTrue();
    }

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
        notifications.ShouldAllBe(item => ReadMessageText(item.Message).EnumerateRunes().Count() <= NotificationMessage.MaxLength);
        rendered.ShouldNotContain("---");
        rendered.ShouldNotContain("────────────");
        rendered.ShouldContain($"{Environment.NewLine}• • •{Environment.NewLine}{Environment.NewLine}🔥");
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
            new Dictionary<string, string>(),
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
            new Dictionary<string, string>(),
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
            new Dictionary<string, string>(),
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
            new Dictionary<string, string>(),
            "log-smoke-fingerprint",
            1);

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Topic.ShouldBe("tests");
    }

    [Fact(DisplayName = "Compose log event should render message exceptions and fields in one block when error is structured")]
    public void ComposeLogEvent_Should_RenderMessageExceptionsAndFieldsInOneBlock_When_ErrorIsStructured()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var timestamp = new DateTimeOffset(2026, 9, 2, 17, 8, 33, TimeSpan.Zero);
        var logEvent = new LogEvent(
            Timestamp: timestamp,
            Service: "grafana",
            Namespace: "observability",
            Container: "grafana",
            Owner: null,
            Severity: "error",
            Message: "Failed to read data sources",
            ExceptionType: null,
            StackTrace: null,
            TraceId: null,
            Fields: new Dictionary<string, string>
            {
                ["logger"] = "infra.usagestats.collector",
                ["error"] = "plugin <not found>",
                ["details"] = "Тест \"кавычек\"\nC:\\temp"
            },
            Fingerprint: "grafana-structured-fields",
            Occurrences: 1,
            StreamId: "0000007b000001c850d9950ea6196b1a4812081265faa1c7");

        var notification = composer.ComposeLogEvent(timestamp, logEvent);

        notification.Message.ShouldContain("🔴 <b>ERROR · grafana</b>");
        notification.Message.ShouldContain("📦 observability/grafana");
        notification.Message.ShouldContain("🕒 2026-09-02 17:08:33 UTC");
        notification.Message.ShouldNotContain("🏷 <b>Fields</b>");
        notification.Message.ShouldContain("plugin &lt;not found&gt;");
        WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n").ShouldContain(
            """
            <pre>message: Failed to read data sources

            exceptions:
            [
              {
                "Depth": 0,
                "Message": "plugin <not found>"
              }
            ]

            fields:
            {
              "details": "Тест \"кавычек\"\nC:\\temp",
              "logger": "infra.usagestats.collector"
            }</pre>
            """.ReplaceLineEndings("\n"));
        notification.Message.ShouldContain("Logs for this source and window");
        notification.Message.ShouldContain($"</pre>{Environment.NewLine}{Environment.NewLine}🔗 ");
        notification.Message.ShouldContain("%22queryType%22%3A%22instant%22");
        notification.Message.ShouldContain(
            "_stream_id%3A0000007b000001c850d9950ea6196b1a4812081265faa1c7");
        notification.Message.ShouldContain(
            timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Theory(DisplayName = "Compose log event should render legacy exception names when exception details are available")]
    [InlineData("exception.message")]
    [InlineData("error")]
    [InlineData("err")]
    public void ComposeLogEvent_Should_RenderLegacyExceptionNames_When_ExceptionDetailsAreAvailable(string messageField)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        const string exceptionMessage = "Не найден контроллер <test> \"details\" 🙂";
        const string stackTrace = "at Controller.Load()\n  at C:\\src\\Program.cs:42";
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Application_Error",
                ["severity_text"] = "Error",
                ["service.name"] = "legacy-crm",
                ["exception.type"] = "System.Web.HttpException",
                [messageField] = exceptionMessage,
                ["exception.stacktrace"] = stackTrace,
                ["EnvironmentName"] = "Production"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        var exceptionsStart = decoded.IndexOf("exceptions:\n", StringComparison.Ordinal);
        var fieldsStart = decoded.IndexOf("\n\nfields:", StringComparison.Ordinal);
        exceptionsStart.ShouldBeGreaterThan(decoded.IndexOf("message:", StringComparison.Ordinal));
        fieldsStart.ShouldBeGreaterThan(exceptionsStart);
        using var document = JsonDocument.Parse(decoded[(exceptionsStart + "exceptions:\n".Length)..fieldsStart]);
        var exception = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
        exception.GetProperty("ClassName").GetString().ShouldBe("System.Web.HttpException");
        exception.GetProperty("Message").GetString().ShouldBe(exceptionMessage);
        exception.GetProperty("StackTraceString").GetString().ShouldBe(stackTrace);
        exception.GetProperty("Depth").GetInt32().ShouldBe(0);
        exception.TryGetProperty("HResult", out _).ShouldBeFalse();
        exception.TryGetProperty("Source", out _).ShouldBeFalse();
        decoded[fieldsStart..].ShouldNotContain(messageField);
        decoded.Split("<pre>").Length.ShouldBe(2);
        decoded.Split("</pre>").Length.ShouldBe(2);
        notification.Message.ShouldNotContain("⚠️");
        notification.Message.ShouldContain("&lt;test&gt;");
    }

    [Fact(DisplayName = "Compose log event should render nested exceptions in one block when stack contains inner exceptions")]
    public void ComposeLogEvent_Should_RenderNestedExceptionsInOneBlock_When_StackContainsInnerExceptions()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        const string stack = """
            System.InvalidOperationException: Save failed
             ---> System.IO.IOException: Connection failed
             ---> System.TimeoutException: Timeout <database> "details" 🙂
               at Database.Read()
               --- End of inner exception stack trace ---
               at Connection.Open()
               --- End of inner exception stack trace ---
               at Orders.Save()
            """;
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Could not process order",
                ["severity_text"] = "Error",
                ["exception.type"] = "InvalidOperationException",
                ["exception.message"] = "Save failed",
                ["exception.stacktrace"] = stack,
                ["exception.hresult"] = "-2146233079",
                ["exception.source"] = "Orders",
                ["OrderId"] = "123"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        using var document = ReadExceptions(notification.Message);
        var exceptions = document.RootElement.EnumerateArray().ToArray();
        exceptions.Select(item => item.GetProperty("Depth").GetInt32()).ShouldBe([0, 1, 2]);
        exceptions.Select(item => item.GetProperty("ClassName").GetString()).ShouldBe(
            ["InvalidOperationException", "System.IO.IOException", "System.TimeoutException"]);
        exceptions.Select(item => item.GetProperty("Message").GetString()).ShouldBe(
            ["Save failed", "Connection failed", "Timeout <database> \"details\" 🙂"]);
        exceptions.Select(item => item.GetProperty("StackTraceString").GetString()).ShouldBe(
            ["   at Orders.Save()", "   at Connection.Open()", "   at Database.Read()"]);
        exceptions[0].GetProperty("HResult").GetInt32().ShouldBe(-2146233079);
        exceptions[0].GetProperty("Source").GetString().ShouldBe("Orders");
        exceptions[1].TryGetProperty("HResult", out _).ShouldBeFalse();
        exceptions[2].TryGetProperty("Source", out _).ShouldBeFalse();
        notification.Message.ShouldContain("&lt;database&gt;");
        notification.Message.ShouldContain("message: Could not process order");
        notification.Message.ShouldContain("OrderId");
        notification.Message.Split("<pre>").Length.ShouldBe(2);
        notification.Message.Split("</pre>").Length.ShouldBe(2);
    }

    [Fact(DisplayName = "Compose log event should preserve extra exception context when stack header contains additional details")]
    public void ComposeLogEvent_Should_PreserveExtraExceptionContext_When_StackHeaderContainsAdditionalDetails()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        const string stack = """
            System.IO.FileNotFoundException: Load failed
            File name: 'settings.json'
             ---> System.IO.IOException: Disk error
               --- End of inner exception stack trace ---
            """;
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Request failed",
                ["severity_text"] = "Error",
                ["exception.message"] = "Load failed",
                ["exception.stacktrace"] = stack
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        using var document = ReadExceptions(notification.Message);
        document.RootElement[0].GetProperty("Message").GetString().ShouldBe("Load failed\nFile name: 'settings.json'");
        document.RootElement[1].GetProperty("Message").GetString().ShouldBe("Disk error");
    }

    [Fact(DisplayName = "Compose log event should preserve original stack when nested exception text is incomplete")]
    public void ComposeLogEvent_Should_PreserveOriginalStack_When_NestedExceptionTextIsIncomplete()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        const string stack = "System.Exception: Outer\n ---> System.Exception: Truncated";
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Request failed",
                ["severity_text"] = "Error",
                ["exception.stacktrace"] = stack
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        using var document = ReadExceptions(notification.Message);
        var exception = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
        exception.GetProperty("Depth").GetInt32().ShouldBe(0);
        exception.GetProperty("StackTraceString").GetString().ShouldBe(stack);
    }

    [Theory(DisplayName = "Compose log event should bound nested exceptions without breaking JSON when chain exceeds message budget")]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1750)]
    [InlineData(5000)]
    public void ComposeLogEvent_Should_BoundNestedExceptionsWithoutBreakingJson_When_ChainExceedsMessageBudget(int logsUrlPadding)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions
            {
                GrafanaLogsUrl = "https://grafana.example/" + new string('a', logsUrlPadding)
            }));
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var content = string.Concat(Enumerable.Repeat("Ошибка \"<&>\" \\ 🙂", 120));
        var stack = "System.Exception: " + content
                    + string.Concat(Enumerable.Repeat("\n ---> System.Exception: " + content, 19))
                    + "\n   at Cause.Read()"
                    + string.Concat(Enumerable.Repeat("\n   --- End of inner exception stack trace ---\n   at Wrapper.Run()", 19));
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = new string('m', 2000),
                ["severity_text"] = "Error",
                ["service.name"] = content,
                ["k8s.namespace.name"] = content,
                ["k8s.container.name"] = content,
                ["trace_id"] = content,
                ["exception.message"] = content,
                ["exception.stacktrace"] = stack,
                ["exception.hresult"] = "-2147467259",
                ["exception.source"] = content,
                ["EnvironmentName"] = "Production"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        using var document = ReadExceptions(notification.Message);
        var exceptions = document.RootElement.EnumerateArray().ToArray();
        exceptions.Length.ShouldBeInRange(2, 19);
        exceptions[0].GetProperty("Depth").GetInt32().ShouldBe(0);
        exceptions[^1].GetProperty("Depth").GetInt32().ShouldBe(19);
        foreach (var exception in exceptions)
        {
            var message = exception.GetProperty("Message").GetString().ShouldNotBeNull();
            message.ShouldNotContain("\uFFFD");
            message.ShouldEndWith("…");
            content.ShouldStartWith(message[..^1]);
        }

        notification.Message.ShouldContain($"{20 - exceptions.Length} exceptions omitted");
        notification.Message.Split("<pre>").Length.ShouldBe(2);
        notification.Message.Split("</pre>").Length.ShouldBe(2);
    }

    [Theory(DisplayName = "Compose log event should render optional exception metadata when explicit fields are present")]
    [InlineData("-2147467259", "System.Web", -2147467259)]
    [InlineData("0", "", 0)]
    [InlineData("", "My <service> \"assembly\"", null)]
    [InlineData(" ", " ", null)]
    [InlineData("invalid", "", null)]
    [InlineData("2147483648", "", null)]
    public void ComposeLogEvent_Should_RenderOptionalExceptionMetadata_When_ExplicitFieldsArePresent(
        string hResult,
        string source,
        int? expectedHResult)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Request failed",
                ["severity_text"] = "Error",
                ["exception.hresult"] = hResult,
                ["exception.source"] = source,
                ["source"] = "stdout",
                ["HResult"] = "generic context"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        var start = decoded.IndexOf("exceptions:\n", StringComparison.Ordinal);
        var fieldsStart = decoded.IndexOf("fields:\n", StringComparison.Ordinal);
        if (expectedHResult.HasValue || !string.IsNullOrWhiteSpace(source))
        {
            start.ShouldBeGreaterThan(0);
            using var document = JsonDocument.Parse(decoded[(start + "exceptions:\n".Length)..fieldsStart]);
            var exception = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
            exception.TryGetProperty("HResult", out var hResultProperty).ShouldBe(expectedHResult.HasValue);
            if (expectedHResult.HasValue)
            {
                hResultProperty.GetInt32().ShouldBe(expectedHResult.Value);
                decoded[fieldsStart..].ShouldNotContain("\"exception.hresult\":");
            }

            exception.TryGetProperty("Source", out var sourceProperty).ShouldBe(!string.IsNullOrWhiteSpace(source));
            if (!string.IsNullOrWhiteSpace(source))
            {
                sourceProperty.GetString().ShouldBe(source);
                decoded[fieldsStart..].ShouldNotContain("\"exception.source\":");
            }
        }
        else
        {
            start.ShouldBe(-1);
            if (!string.IsNullOrWhiteSpace(hResult))
            {
                decoded[fieldsStart..].ShouldContain($"\"exception.hresult\": \"{hResult}\"");
            }
        }

        decoded[fieldsStart..].ShouldContain("\"source\": \"stdout\"");
        decoded[fieldsStart..].ShouldContain("\"HResult\": \"generic context\"");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Fact(DisplayName = "Compose log event should preserve distinct error fields when exception message is provided")]
    public void ComposeLogEvent_Should_PreserveDistinctErrorFields_When_ExceptionMessageIsProvided()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Request failed",
                ["severity_text"] = "Error",
                ["exception.message"] = "Primary failure",
                ["error"] = "Primary failure",
                ["err"] = "Additional context"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        decoded.ShouldContain("\"Message\": \"Primary failure\"");
        decoded.ShouldContain("\"err\": \"Additional context\"");
        decoded.ShouldNotContain("\"error\":");
        decoded.ShouldNotContain("\"exception.message\":");
    }

    [Fact(DisplayName = "Compose log event should omit exceptions when no exception details are available")]
    public void ComposeLogEvent_Should_OmitExceptions_When_NoExceptionDetailsAreAvailable()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = "Request failed",
                ["severity_text"] = "Error",
                ["exception.message"] = " ",
                ["EnvironmentName"] = "Production"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        notification.Message.ShouldContain("message: Request failed");
        notification.Message.ShouldContain("fields:");
        notification.Message.ShouldNotContain("exceptions:");
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
            new Dictionary<string, string>(),
            "metrics-server-timeout",
            5);

        var notification = composer.ComposeLogEvent(windowStart, logEvent);

        notification.Message.ShouldContain(
            "At least <b>5 matching events</b> in the 1-minute window "
            + "2026-09-01 17:46:00–2026-09-01 17:47:00 UTC");
    }

    [Fact(DisplayName = "Compose log event should stay within Telegram limit when encoded content is long")]
    public void ComposeLogEvent_Should_StayWithinTelegramLimit_When_EncodedContentIsLong()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var windowStart = new DateTimeOffset(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
        var encodedContent = string.Concat(Enumerable.Repeat("<&>\"'", 1000));
        var logEvent = new LogEvent(
            Timestamp: windowStart.AddSeconds(20),
            Service: encodedContent,
            Namespace: encodedContent,
            Container: encodedContent,
            Owner: null,
            Severity: "error",
            Message: encodedContent,
            ExceptionType: encodedContent,
            StackTrace: encodedContent,
            TraceId: encodedContent,
            Fields: new Dictionary<string, string>
            {
                ["exception.message"] = encodedContent,
                ["exception.hresult"] = "-2147467259",
                ["exception.source"] = encodedContent,
                ["UserId"] = encodedContent,
                ["UserName"] = encodedContent
            },
            Fingerprint: "long-encoded-content",
            Occurrences: 3,
            StreamId: "0000007b000001c850d9950ea6196b1a4812081265faa1c7");

        var notification = composer.ComposeLogEvent(windowStart, logEvent);

        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        notification.Message.ShouldContain("Logs for this source and window");
    }

    [Theory(DisplayName = "Compose log event should preserve valid exception JSON when content exceeds message budget")]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1750)]
    [InlineData(5000)]
    public void ComposeLogEvent_Should_PreserveValidExceptionJson_When_ContentExceedsMessageBudget(int logsUrlPadding)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions
            {
                GrafanaLogsUrl = "https://grafana.example/" + new string('a', logsUrlPadding)
            }));
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var content = string.Concat(Enumerable.Repeat("Ошибка \"<&>\" \\ \n🙂", 200));
        var logEvent = normalizer.Normalize(
        [
            new Dictionary<string, string>
            {
                ["_msg"] = new string('m', 2000),
                ["severity_text"] = "Error",
                ["exception.type"] = content,
                ["exception.message"] = content,
                ["exception.stacktrace"] = content,
                ["exception.hresult"] = "-2147467259",
                ["exception.source"] = content,
                ["EnvironmentName"] = "Production"
            }
        ]).ShouldHaveSingleItem();

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        using var document = ReadExceptions(notification.Message);
        var exception = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
        exception.GetProperty("HResult").GetInt32().ShouldBe(-2147467259);
        exception.GetProperty("Source").ValueKind.ShouldBe(JsonValueKind.String);
        foreach (var property in exception.EnumerateObject())
        {
            if (property.NameEquals("HResult") || property.NameEquals("Depth"))
            {
                continue;
            }

            var value = property.Value.GetString().ShouldNotBeNull();
            value.ShouldNotContain("\uFFFD");
            if (value == "… (stack trace omitted)")
            {
                continue;
            }

            value.ShouldEndWith("…");
            content.ShouldStartWith(value[..^1]);
        }

        decoded.Split("<pre>").Length.ShouldBe(2);
        decoded.Split("</pre>").Length.ShouldBe(2);
    }

    [Theory(DisplayName = "Compose log event should preserve valid JSON when fields exceed message budget")]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(1750)]
    [InlineData(5000)]
    public void ComposeLogEvent_Should_PreserveValidJson_When_FieldsExceedMessageBudget(
        int logsUrlPadding)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions
            {
                GrafanaLogsUrl = "https://grafana.example/" + new string('a', logsUrlPadding)
            }));
        var fields = Enumerable.Range(0, 30).ToDictionary(
            index => $"Field\"{index:D2}",
            index => $"Значение {index}: <&>\"'\\\n🙂");
        fields.Add("A", new string('x', 1000));
        fields.Add(new string('z', 1000), "oversized key");
        var windowStart = new DateTimeOffset(2026, 9, 27, 17, 0, 0, TimeSpan.Zero);
        var logEvent = new LogEvent(
            Timestamp: windowStart,
            Service: new string('s', 180),
            Namespace: new string('n', 124),
            Container: new string('c', 125),
            Owner: "tests",
            Severity: "error",
            Message: new string('m', 1000),
            ExceptionType: new string('e', 180),
            StackTrace: new string('s', 1000),
            TraceId: new string('t', 180),
            Fields: fields,
            Fingerprint: "json-fields-truncation",
            Occurrences: 3);

        var notification = composer.ComposeLogEvent(windowStart, logEvent);

        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        var fieldsStart = notification.Message.IndexOf("fields:", StringComparison.Ordinal);
        fieldsStart.ShouldBeGreaterThanOrEqualTo(0);
        var fieldsEnd = notification.Message.IndexOf("</pre>", fieldsStart, StringComparison.Ordinal);
        var encodedFields = notification.Message[fieldsStart..fieldsEnd];
        var decodedFields = WebUtility.HtmlDecode(encodedFields).ReplaceLineEndings("\n");
        var noticeStart = decodedFields.LastIndexOf("\n… (", StringComparison.Ordinal);
        noticeStart.ShouldBeGreaterThan(0);
        var json = decodedFields["fields:\n".Length..noticeStart];
        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().ToArray();
        properties.Length.ShouldBeGreaterThan(0);
        properties.Length.ShouldBeLessThan(fields.Count);
        decodedFields.ShouldEndWith($"\n… ({fields.Count - properties.Length} fields omitted)");
        foreach (var property in properties)
        {
            property.Value.GetString().ShouldBe(fields[property.Name]);
        }
    }

    [Theory(DisplayName = "Compose log event should count only omitted fields when exception metadata is rendered separately")]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    [InlineData(100)]
    public void ComposeLogEvent_Should_CountOnlyOmittedFields_When_ExceptionMetadataIsRenderedSeparately(int omittedCount)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var fields = Enumerable.Range(0, omittedCount).ToDictionary(index => $"Long{index}", _ => new string('x', 10000));
        fields.Add("Small", "retained");
        fields.Add("exception.message", "Cause");
        fields.Add("exception.hresult", "-1");
        fields.Add("exception.source", "Demo");
        var logEvent = CreateLogEvent(fields: fields);

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message);
        decoded.ShouldContain("\"Small\": \"retained\"");
        decoded.ShouldContain($"… ({omittedCount} fields omitted)");
        decoded.ShouldNotContain("some fields omitted");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Theory(DisplayName = "Compose log event should link trace in Grafana when trace id is valid")]
    [InlineData("0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789abcdef")]
    public void ComposeLogEvent_Should_LinkTraceInGrafana_When_TraceIdIsValid(string traceId)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions
            {
                GrafanaLogsUrl = "https://grafana.example/prefix/explore?left=old#fragment",
                WindowSeconds = 120
            }));
        var logEvent = CreateLogEvent(traceId: traceId);

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var traceLine = notification.Message.Split('\n').Single(line => line.StartsWith("🔎 ", StringComparison.Ordinal));
        traceLine.ShouldContain("\">Trace</a>");
        notification.Message.ShouldContain($"</a>{Environment.NewLine}🔎 ");
        var hrefStart = traceLine.IndexOf("href=\"", StringComparison.Ordinal) + "href=\"".Length;
        var hrefEnd = traceLine.IndexOf('"', hrefStart);
        var uri = new Uri(WebUtility.HtmlDecode(traceLine[hrefStart..hrefEnd]));
        uri.GetLeftPart(UriPartial.Path).ShouldBe("https://grafana.example/prefix/explore");
        uri.Fragment.ShouldBeEmpty();
        var parameters = System.Web.HttpUtility.ParseQueryString(uri.Query);
        parameters["schemaVersion"].ShouldBe("1");
        parameters["left"].ShouldBeNull();
        using var document = JsonDocument.Parse(parameters["panes"].ShouldNotBeNull());
        var pane = document.RootElement.GetProperty("trace");
        pane.GetProperty("datasource").GetString().ShouldBe("victoriatraces");
        var query = pane.GetProperty("queries")[0];
        query.GetProperty("datasource").GetProperty("type").GetString().ShouldBe("jaeger");
        query.GetProperty("datasource").GetProperty("uid").GetString().ShouldBe("victoriatraces");
        query.GetProperty("query").GetString().ShouldBe(traceId);
        query.TryGetProperty("queryType", out _).ShouldBeFalse();
        pane.GetProperty("range").GetProperty("from").GetString()
            .ShouldBe(logEvent.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        pane.GetProperty("range").GetProperty("to").GetString()
            .ShouldBe(logEvent.Timestamp.AddMinutes(2).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        notification.Message.ShouldContain("Logs for this source and window");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Theory(DisplayName = "Compose log event should keep trace as text when safe trace link cannot be built")]
    [InlineData("https://grafana.example/explore", "invalid<&>trace")]
    [InlineData("https://grafana.example/explore", "0123456789abcde")]
    [InlineData("", "0123456789abcdef")]
    [InlineData("/explore", "0123456789abcdef")]
    [InlineData("file:///tmp/explore", "0123456789abcdef")]
    public void ComposeLogEvent_Should_KeepTraceAsText_When_SafeTraceLinkCannotBeBuilt(string grafanaUrl, string traceId)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions { GrafanaLogsUrl = grafanaUrl }));
        var logEvent = CreateLogEvent(traceId: traceId);

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var traceLine = notification.Message.Split('\n').Single(line => line.StartsWith("🔎 ", StringComparison.Ordinal));
        traceLine.ShouldNotContain("<a ");
        WebUtility.HtmlDecode(traceLine).ShouldContain($"<code>{traceId}</code>");
        if (grafanaUrl.Length == 0)
        {
            notification.Message.ShouldContain($"</pre>{Environment.NewLine}{Environment.NewLine}🔎 ");
        }
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Fact(DisplayName = "Compose log event should preserve links and fields when html exceeds text limit")]
    public void ComposeLogEvent_Should_PreserveLinksAndFields_When_HtmlExceedsTextLimit()
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions { GrafanaLogsUrl = "https://grafana.example/" + new string('a', 5000) }));
        var logEvent = CreateLogEvent(traceId: "0123456789abcdef", fields: new Dictionary<string, string> { ["Detail"] = "Value" });

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        notification.Message.ShouldContain(">Trace</a>");
        notification.Message.ShouldContain(">Logs for this source and window</a>");
        notification.Message.ShouldContain("fields:");
        notification.Message.ShouldNotContain("fields omitted");
        notification.Message.Length.ShouldBeGreaterThan(4096);
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
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
            new Dictionary<string, string>(),
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

    private static string ReadMessageText(string html) => XElement.Parse("<message>" + html.Replace("\r", "&#13;", StringComparison.Ordinal) + "</message>", LoadOptions.PreserveWhitespace).Value;

    [Fact(DisplayName = "Compose log event should return unused space to message when oversized fields are omitted")]
    public void ComposeLogEvent_Should_ReturnUnusedSpaceToMessage_When_OversizedFieldsAreOmitted()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var logEvent = CreateLogEvent(fields: new Dictionary<string, string> { ["Huge"] = new string('f', 10000) })
            with
        { Message = new string('m', 8000) };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);
        var text = ReadMessageText(notification.Message);

        text.EnumerateRunes().Count().ShouldBe(4096);
        text.ShouldContain(new string('m', 3500));
        text.ShouldContain("1 fields omitted");
        notification.Message.ShouldContain(">Logs for this source and window</a>");
    }

    [Fact(DisplayName = "Compose log event should preserve every exception when small chain exceeds old entry cap")]
    public void ComposeLogEvent_Should_PreserveEveryException_When_SmallChainExceedsOldEntryCap()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var stack = "System.Exception: Outer" + string.Concat(Enumerable.Repeat("\n ---> System.Exception: Cause", 5))
                    + "\n   at Cause.Read()" + string.Concat(Enumerable.Repeat("\n   --- End of inner exception stack trace ---\n   at Wrapper.Run()", 5));
        var logEvent = CreateLogEvent() with { StackTrace = stack };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);
        using var exceptions = ReadExceptions(notification.Message);

        exceptions.RootElement.GetArrayLength().ShouldBe(6);
        notification.Message.ShouldNotContain("omitted");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(4096);
    }

    [Fact(DisplayName = "Compose log event should preserve all details when full text fits telegram limit")]
    public void ComposeLogEvent_Should_PreserveAllDetails_When_FullTextFitsTelegramLimit()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var fields = Enumerable.Range(0, 15).ToDictionary(index => $"Field{index}", _ => new string('я', 60));
        var logEvent = CreateLogEvent(fields: fields) with { Message = new string('я', 1200), StackTrace = new string('s', 800) };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);
        var text = ReadMessageText(notification.Message);

        text.ShouldContain(logEvent.Message);
        text.ShouldContain(logEvent.StackTrace);
        text.ShouldNotContain("omitted");
        text.ShouldNotContain("…");
        foreach (var field in fields)
        {
            text.ShouldContain($"\"{field.Key}\": \"{field.Value}\"");
        }
        text.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(4096);
        notification.Message.Length.ShouldBeGreaterThan(4096);
    }

    [Theory(DisplayName = "Compose log event should reuse unused detail space when only one block is large")]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposeLogEvent_Should_ReuseUnusedDetailSpace_When_OnlyOneBlockIsLarge(bool largeException)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var fields = largeException ? new Dictionary<string, string> { ["Small"] = "retained" }
            : Enumerable.Range(0, 100).ToDictionary(index => $"Field{index:D2}", _ => new string('f', 80));
        var logEvent = CreateLogEvent(fields: fields) with { StackTrace = largeException ? new string('s', 8000) : null };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);
        var text = ReadMessageText(notification.Message);

        text.EnumerateRunes().Count().ShouldBeInRange(3950, 4096);
        if (largeException)
        {
            using var exceptions = ReadExceptions(notification.Message);
            exceptions.RootElement[0].GetProperty("StackTraceString").GetString()!.Length.ShouldBeGreaterThan(3000);
            text.ShouldContain("retained");
        }
        else
        {
            text.ShouldContain("fields omitted");
            text.ShouldContain("Field30");
        }
    }

    [Fact(DisplayName = "Compose log event should use exact text limit when message alone is oversized")]
    public void ComposeLogEvent_Should_UseExactTextLimit_When_MessageAloneIsOversized()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var logEvent = CreateLogEvent() with { Message = string.Concat(Enumerable.Repeat("Я🙂<&>", 2000)), ExceptionType = null, StackTrace = null };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);
        var text = ReadMessageText(notification.Message);

        text.EnumerateRunes().Count().ShouldBe(4096);
        text.ShouldNotContain("\uFFFD");
        text.ShouldContain("…");
    }

    [Theory(DisplayName = "Compose metric alerts should use available space when description exceeds old limit")]
    [InlineData(2500)]
    [InlineData(8000)]
    public void ComposeMetricAlerts_Should_UseAvailableSpace_When_DescriptionExceedsOldLimit(int descriptionLength)
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var alert = CreateAlert("tests", "long-description") with
        {
            Annotations = new Dictionary<string, string>
            {
                ["description"] = new string('я', descriptionLength),
                ["dashboard_url"] = "https://grafana.example/" + new string('x', 2000)
            }
        };

        var message = composer.ComposeMetricAlerts("firing", "https://alertmanager.example", [alert], DateTimeOffset.UtcNow).Single().Message;
        var text = ReadMessageText(message);

        message.ShouldContain(">Grafana</a>");
        message.ShouldContain(">Alertmanager</a>");
        text.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(4096);
        if (descriptionLength == 2500)
        {
            text.ShouldContain(new string('я', descriptionLength));
            text.ShouldNotContain("…");
        }
        else
        {
            text.EnumerateRunes().Count().ShouldBe(4096);
            text.ShouldContain("…");
        }
    }

    private static JsonDocument ReadExceptions(string message)
    {
        var decoded = WebUtility.HtmlDecode(message).ReplaceLineEndings("\n");
        var start = decoded.IndexOf("exceptions:\n", StringComparison.Ordinal) + "exceptions:\n".Length;
        var end = decoded.IndexOf("\n]", start, StringComparison.Ordinal) + 2;
        return JsonDocument.Parse(decoded[start..end]);
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

    [Theory(DisplayName = "Compose log event should link the exact record when log record uid is present")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", null)]
    [InlineData("550e8400-e29b-41d4-a716-446655440000", "0000007b000001c850d9950ea6196b1a4812081265faa1c7")]
    [InlineData("record\" OR * \\ \n <value>", null)]
    public void ComposeLogEvent_Should_LinkExactRecord_When_LogRecordUidIsPresent(string recordUid, string? streamId)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions
            {
                GrafanaLogsUrl = "https://grafana.example/prefix/explore?left=old#fragment",
                WindowSeconds = 60
            }));
        var logEvent = CreateLogEvent(fields: new Dictionary<string, string> { ["log.record.uid"] = recordUid })
            with
        {
            StreamId = streamId
        };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message);
        decoded.ShouldContain("\"log.record.uid\":");
        decoded.ShouldContain(">Log</a>");
        decoded.ShouldContain($"</pre>{Environment.NewLine}{Environment.NewLine}🔗 ");
        decoded.ShouldNotContain("matching events");
        decoded.ShouldNotContain("Logs for this source and window");
        var hrefStart = decoded.IndexOf("href=\"", StringComparison.Ordinal) + "href=\"".Length;
        var hrefEnd = decoded.IndexOf('"', hrefStart);
        var uri = new Uri(decoded[hrefStart..hrefEnd]);
        uri.GetLeftPart(UriPartial.Path).ShouldBe("https://grafana.example/prefix/explore");
        uri.Fragment.ShouldBeEmpty();
        var parameters = System.Web.HttpUtility.ParseQueryString(uri.Query);
        using var panes = JsonDocument.Parse(parameters["panes"].ShouldNotBeNull());
        var pane = panes.RootElement.GetProperty("logs");
        var query = pane.GetProperty("queries")[0];
        query.GetProperty("queryType").GetString().ShouldBe("instant");
        query.GetProperty("expr").GetString().ShouldBe($"log.record.uid:={JsonSerializer.Serialize(recordUid)}");
        query.GetProperty("query").GetString().ShouldBe(query.GetProperty("expr").GetString());
        pane.GetProperty("range").GetProperty("from").GetString()
            .ShouldBe(logEvent.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        pane.GetProperty("range").GetProperty("to").GetString()
            .ShouldBe(logEvent.Timestamp.AddMinutes(1).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Theory(DisplayName = "Compose log event should preserve the record UID when fields or links exceed message budget")]
    [InlineData(0)]
    [InlineData(5000)]
    public void ComposeLogEvent_Should_PreserveRecordUid_When_FieldsOrLinksExceedMessageBudget(int logsUrlPadding)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions { GrafanaLogsUrl = "https://grafana.example/" + new string('a', logsUrlPadding) }));
        const string recordUid = "550e8400-e29b-41d4-a716-446655440000";
        var fields = Enumerable.Range(1, 12).ToDictionary(index => $"Detail{index:D2}", _ => new string('x', 10000));
        fields["log.record.uid"] = recordUid;
        var logEvent = CreateLogEvent(fields: fields);

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        decoded.ShouldContain($"\"log.record.uid\": \"{recordUid}\"");
        decoded.ShouldContain("… (12 fields omitted)");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        var start = decoded.IndexOf("fields:\n", StringComparison.Ordinal) + "fields:\n".Length;
        var end = decoded.IndexOf("\n… (", start, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(decoded[start..end]);
        json.RootElement.GetProperty("log.record.uid").GetString().ShouldBe(recordUid);
    }

    [Theory(DisplayName = "Compose log event should retain the group link without a trace when all record ids are available")]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(10000)]
    public void ComposeLogEvent_Should_RetainGroupLinkWithoutTrace_When_AllRecordIdsAreAvailable(int recordCount)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
            Options.Create(new MetricAlertsOptions()),
            scope.ServiceProvider.GetRequiredService<ITopicRouter>(),
            Options.Create(new VictoriaLogsOptions { GrafanaLogsUrl = "https://grafana.panixida.ru/explore" }));
        var ids = Enumerable.Range(0, recordCount).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var logEvent = CreateLogEvent("0123456789abcdef0123456789abcdef", new Dictionary<string, string>
        {
            ["log.record.uid"] = ids[0],
            ["Detail"] = new string('x', 5000),
            ["exception.message"] = new string('x', 5000)
        }) with
        {
            Occurrences = recordCount,
            RecordUids = ids,
            Message = new string('x', 5000),
            ExceptionType = "System.InvalidOperationException",
            StackTrace = new string('x', 5000)
        };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        var decoded = WebUtility.HtmlDecode(notification.Message);
        decoded.ShouldContain(">Logs</a>");
        decoded.ShouldContain($"</pre>{Environment.NewLine}{Environment.NewLine}🔗 ");
        decoded.ShouldNotContain("🔎");
        decoded.ShouldContain($"\"log.record.uid\": \"{ids[0]}\"");
        decoded.ShouldContain($"At least <b>{recordCount} matching events</b>");
        ReadMessageText(notification.Message).EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
    }

    [Fact(DisplayName = "Compose log event should use the source fallback without a trace when group ids are incomplete")]
    public void ComposeLogEvent_Should_UseSourceFallbackWithoutTrace_When_GroupIdsAreIncomplete()
    {
        using var scope = Fixture.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var logEvent = CreateLogEvent("0123456789abcdef", new Dictionary<string, string> { ["log.record.uid"] = "sample-only" })
            with
        { Occurrences = 3, StreamId = "0000007b000001c850d9950ea6196b1a4812081265faa1c7" };

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        notification.Message.ShouldContain(">Logs for this source and window</a>");
        notification.Message.ShouldNotContain("🔎");
        var parameters = System.Web.HttpUtility.ParseQueryString(new Uri(WebUtility.HtmlDecode(
            notification.Message.Split("href=\"", StringSplitOptions.None)[1].Split('"')[0])).Query);
        using var panes = JsonDocument.Parse(parameters["panes"].ShouldNotBeNull());
        panes.RootElement.GetProperty("logs").GetProperty("queries")[0].GetProperty("expr").GetString()
            .ShouldBe($"_stream_id:{logEvent.StreamId}");
    }

    private static LogEvent CreateLogEvent(string? traceId = null, IReadOnlyDictionary<string, string>? fields = null)
    {
        return new LogEvent(
            Timestamp: new DateTimeOffset(2026, 10, 2, 17, 0, 0, TimeSpan.Zero),
            Service: "test-service",
            Namespace: "tests",
            Container: "demo",
            Owner: "tests",
            Severity: "error",
            Message: "Test failure",
            ExceptionType: null,
            StackTrace: null,
            TraceId: traceId,
            Fields: fields ?? new Dictionary<string, string>(),
            Fingerprint: "fields-trace-test",
            Occurrences: 1);
    }
}
