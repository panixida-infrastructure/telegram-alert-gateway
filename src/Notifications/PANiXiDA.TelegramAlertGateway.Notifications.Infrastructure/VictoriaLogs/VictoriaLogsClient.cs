using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal sealed class VictoriaLogsClient(
    HttpClient httpClient,
    IOptions<VictoriaLogsOptions> options)
{
    public const string HttpClientName = "victoria-logs";

    private readonly VictoriaLogsOptions _options = options.Value;

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> QueryAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        var records = new List<IReadOnlyDictionary<string, string>>();
        var offset = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/select/logsql/query");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["query"] = _options.Query,
                ["start"] = startUtc.ToUniversalTime().ToString("O"),
                ["end"] = endUtc.ToUniversalTime().ToString("O"),
                ["limit"] = _options.MaxEntriesPerWindow.ToString(),
                ["offset"] = offset.ToString(),
                ["timeout"] = "30s"
            });

            Authenticate(request);

            using var response = await httpClient.SendAsync(
                request: request,
                completionOption: HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            var pageCount = 0;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }

                MergeStreamFields(fields);
                records.Add(fields);
                pageCount++;
            }

            if (pageCount < _options.MaxEntriesPerWindow)
            {
                return records;
            }

            offset += pageCount;
        }
    }

    public async Task StoreLogGroupAsync(
        DateTimeOffset windowStartUtc,
        LogEvent logEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.GrafanaLogsUrl)
            || LogGroupQuery.Create(windowStartUtc, logEvent)?.GroupId is not { } groupId)
        {
            return;
        }

        foreach (var recordUids in logEvent.RecordUids!.Chunk(100))
        {
            var membership = new Dictionary<string, string>
            {
                ["_time"] = windowStartUtc.ToUniversalTime().ToString("O"),
                ["_msg"] = "Alert log group membership",
                ["service.name"] = LogGroupQuery.MembershipService,
                ["severity_text"] = "Information",
                [LogGroupQuery.GroupIdField] = groupId,
                [LogGroupQuery.RecordUidsField] = JsonSerializer.Serialize(recordUids)
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/insert/jsonline?_stream_fields=service.name");
            request.Content = new StringContent(
                content: JsonSerializer.Serialize(membership) + "\n",
                encoding: Encoding.UTF8,
                mediaType: "application/stream+json");
            Authenticate(request);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private void Authenticate(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private static void MergeStreamFields(IDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("_stream", out var streamValue)
            || string.IsNullOrWhiteSpace(streamValue))
        {
            return;
        }

        foreach (var property in VictoriaLogsStreamParser
                     .Parse(streamValue)
                     .Where(property => !fields.ContainsKey(property.Key)))
        {
            fields[property.Key] = property.Value;
        }
    }
}
