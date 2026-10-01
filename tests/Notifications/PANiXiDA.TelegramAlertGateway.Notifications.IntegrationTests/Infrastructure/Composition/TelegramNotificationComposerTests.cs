using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

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
        notification.Message.ShouldContain(
            "_stream_id%3A0000007b000001c850d9950ea6196b1a4812081265faa1c7");
        notification.Message.ShouldContain(
            timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        notification.Message.Length.ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
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
        exception.TryGetProperty("HResult", out _).ShouldBeFalse();
        exception.TryGetProperty("Source", out _).ShouldBeFalse();
        decoded[fieldsStart..].ShouldNotContain(messageField);
        decoded.Split("<pre>").Length.ShouldBe(2);
        decoded.Split("</pre>").Length.ShouldBe(2);
        notification.Message.ShouldNotContain("⚠️");
        notification.Message.ShouldContain("&lt;test&gt;");
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
        notification.Message.Length.ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
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

        notification.Message.Length.ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
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

        notification.Message.Length.ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        var decoded = WebUtility.HtmlDecode(notification.Message).ReplaceLineEndings("\n");
        var start = decoded.IndexOf("exceptions:\n", StringComparison.Ordinal) + "exceptions:\n".Length;
        var end = decoded.IndexOf("\n\nfields:", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = decoded.IndexOf("</pre>", start, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(decoded[start..end]);
        var exception = document.RootElement.EnumerateArray().ShouldHaveSingleItem();
        exception.GetProperty("HResult").GetInt32().ShouldBe(-2147467259);
        exception.GetProperty("Source").ValueKind.ShouldBe(JsonValueKind.String);
        foreach (var property in exception.EnumerateObject())
        {
            if (property.NameEquals("HResult"))
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
    [InlineData(0, 700)]
    [InlineData(1000, 450)]
    [InlineData(1750, 250)]
    public void ComposeLogEvent_Should_PreserveValidJson_When_FieldsExceedMessageBudget(
        int logsUrlPadding,
        int expectedFieldsBudget)
    {
        using var scope = Fixture.CreateScope();
        var composer = new TelegramNotificationComposer(
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

        notification.Message.Length.ShouldBeLessThanOrEqualTo(NotificationMessage.MaxLength);
        var fieldsStart = notification.Message.IndexOf("fields:", StringComparison.Ordinal);
        fieldsStart.ShouldBeGreaterThanOrEqualTo(0);
        var fieldsEnd = notification.Message.IndexOf("</pre>", fieldsStart, StringComparison.Ordinal);
        var encodedFields = notification.Message[fieldsStart..fieldsEnd];
        encodedFields.Length.ShouldBeLessThanOrEqualTo(expectedFieldsBudget);
        var decodedFields = WebUtility.HtmlDecode(encodedFields).ReplaceLineEndings("\n");
        const string omittedNotice = "\n… (some fields omitted)";
        decodedFields.ShouldEndWith(omittedNotice);
        var json = decodedFields["fields:\n".Length..^omittedNotice.Length];
        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().ToArray();
        properties.Length.ShouldBeGreaterThan(0);
        properties.Length.ShouldBeLessThan(fields.Count);
        document.RootElement.TryGetProperty("A", out _).ShouldBeFalse();
        foreach (var property in properties)
        {
            property.Value.GetString().ShouldBe(fields[property.Name]);
        }
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
