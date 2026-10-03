using System.Net;
using System.Text;
using System.Text.Json;

using DotNet.Testcontainers.Builders;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.MetricAlerts;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests.VictoriaLogs;

public sealed class VictoriaLogsClientTests(IntegrationTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory(DisplayName = "Store log group should select exactly the counted records when the alert link is queried")]
    [InlineData(3)]
    [InlineData(205)]
    public async Task StoreLogGroup_Should_SelectExactlyCountedRecords_When_TheAlertLinkIsQueried(int recordCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var container = new ContainerBuilder("victoriametrics/victoria-logs:v1.50.0")
            .WithPortBinding(9428, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9428).ForPath("/health")))
            .Build();
        await container.StartAsync(cancellationToken);
        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(9428)}") };
        using var scope = Fixture.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        var records = Enumerable.Range(0, recordCount + 1).Select(index => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
        {
            ["_time"] = timestamp.AddSeconds(5).ToString("O"),
            ["_msg"] = "Repeated failure",
            ["service.name"] = "test-service",
            ["severity_text"] = "Error",
            ["log.record.uid"] = index == 0 ? "record\" OR * \\ \n <value>" : Guid.NewGuid().ToString(),
            ["trace_id"] = "0123456789abcdef0123456789abcdef"
        }).ToArray();
        var logEvent = normalizer.Normalize([.. records.Take(recordCount)]).ShouldHaveSingleItem();
        var options = Options.Create(new VictoriaLogsOptions { GrafanaLogsUrl = "https://grafana.example/explore" });
        var client = new VictoriaLogsClient(httpClient, options);
        var composer = new TelegramNotificationComposer(Options.Create(new MetricAlertsOptions()), scope.ServiceProvider.GetRequiredService<ITopicRouter>(), options);
        using var content = new StringContent(string.Join('\n', records.Select(record => JsonSerializer.Serialize(record))) + "\n",
            Encoding.UTF8, "application/stream+json");
        using var inserted = await httpClient.PostAsync("/insert/jsonline?_stream_fields=service.name", content, cancellationToken);
        inserted.EnsureSuccessStatusCode();

        await client.StoreLogGroupAsync(timestamp, logEvent, cancellationToken);
        await client.StoreLogGroupAsync(timestamp, logEvent, cancellationToken);
        var notification = composer.ComposeLogEvent(timestamp, logEvent);
        var decoded = WebUtility.HtmlDecode(notification.Message);
        var href = decoded.Split("href=\"", StringSplitOptions.None)[1].Split('"')[0];
        var parameters = System.Web.HttpUtility.ParseQueryString(new Uri(href).Query);
        using var panes = JsonDocument.Parse(parameters["panes"].ShouldNotBeNull());
        var query = panes.RootElement.GetProperty("logs").GetProperty("queries")[0].GetProperty("expr").GetString().ShouldNotBeNull();
        var queryClient = new VictoriaLogsClient(httpClient, Options.Create(new VictoriaLogsOptions { Query = query, MaxEntriesPerWindow = 100 }));
        IReadOnlyList<IReadOnlyDictionary<string, string>> selected = [];
        for (var attempt = 0; attempt < 30 && selected.Count != recordCount; attempt++)
        {
            selected = await queryClient.QueryAsync(timestamp, timestamp.AddMinutes(1), cancellationToken);
            if (selected.Count != recordCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        selected.Count.ShouldBe(recordCount);
        selected.Select(record => record["log.record.uid"]).Order(StringComparer.Ordinal)
            .ShouldBe(logEvent.RecordUids);
        var unrelatedId = records[^1]["log.record.uid"];
        selected.ShouldNotContain(record => record["log.record.uid"] == unrelatedId);
        decoded.ShouldContain(">Logs</a>");
        decoded.ShouldNotContain("🔎");
        if (recordCount > 100)
        {
            query.ShouldContain("| unroll alert.group.record_uids");
            var changedGroup = logEvent with { RecordUids = [.. logEvent.RecordUids!, unrelatedId] };
            var (changedQuery, _) = LogGroupQuery.Create(timestamp, changedGroup).ShouldNotBeNull();
            changedQuery.ShouldNotBe(query);
            await client.StoreLogGroupAsync(timestamp, changedGroup, cancellationToken);
            var zoomed = await queryClient.QueryAsync(timestamp.AddSeconds(1), timestamp.AddSeconds(10), cancellationToken);
            zoomed.Select(record => record["log.record.uid"]).Order(StringComparer.Ordinal).ShouldBe(logEvent.RecordUids);
        }
        else
        {
            query.ShouldBe($"log.record.uid:in({JsonSerializer.Serialize(logEvent.RecordUids)[1..^1]})");
        }
    }
}
