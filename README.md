# Telegram Alert Gateway

`telegram-alert-gateway` is the single Telegram delivery boundary for PANiXiDA alerts.
It routes every alert to exactly one owner topic, persists delivery state in PostgreSQL,
and sends messages through `Telegram.Bot`.

## Behavior

- Alertmanager sends metric and health alert webhooks to
  `POST /api/v1/webhooks/alertmanager` with a bearer token.
- Metric alerts preserve `firing` and `resolved` states. Large groups are split into
  multiple Telegram messages; alerts are never silently omitted.
- Each metric alert uses a `Grafana` link from `dashboard_url` (or `logs_url`),
  preserving the rule's panel, time range and URL-encoded filters. Without these
  annotations, `MetricAlerts:GrafanaDashboardUrl` supplies the overview; an internal
  generator URL is not presented as Grafana. URLs are never truncated: invalid
  links or links over the 1000-character HTML budget are omitted.
- `MetricAlerts:AlertmanagerUrl` overrides the webhook's internal `externalURL`.
  Production opens Grafana's active notifications with the external `Alertmanager`
  datasource selected. An empty setting retains the webhook URL when it is a valid
  HTTP(S) link within the 350-character HTML footer budget.
- The gateway polls completed VictoriaLogs windows for error events. Repeated copies
  of one normalized error are combined into one message with an `At least N matching
  events` count and explicit window boundaries. Copies received through multiple
  ingestion paths are not counted twice; different errors remain separate messages.
- A log message includes service, Kubernetes namespace/container, error text,
  exception type, the top of the stack trace, trace id, generic structured fields,
  and a Grafana `Log` link matching `log.record.uid` exactly within the aggregation
  window. Grouped alerts have a `Logs` link selecting all IDs counted in that group
  with `in(...)`, and no trace link. Message, exceptions, fields and the singular
  `log.record.uid` describe the representative record, not every member of the group.
  Records or groups without complete IDs retain the source stream/window link.
  Values whose field names indicate secrets or credentials
  are redacted before Telegram rendering. Optional sections are reduced before the
  message can exceed Telegram's delivery limit.
- The copyable log block contains `message`, then `exceptions`, then `fields`.
  Truncated fields report the exact omitted count, including when the entire fields
  block is removed. Exception metadata rendered separately is excluded from that count.
  `log.record.uid` is prioritized in `fields`; collector-generated UUIDs remain
  copyable even when the final message budget requires dropping links and other fields.
  Explicit `exception.hresult` (a signed 32-bit decimal integer) and
  `exception.source` fields are rendered as optional `HResult` and `Source` in
  `exceptions`, without duplication in `fields`. Invalid HRESULT values stay in
  `fields`; generic `source` and `HResult` fields are not reclassified.
- Each rendered exception has a numeric `Depth`: zero for the outer exception,
  increasing for each nested cause. The gateway parses the standard invariant
  .NET `Exception.ToString()` representation in `exception.stacktrace`, including
  `AggregateException` branches (siblings keep the same depth). Each entry has its
  own type, message and stack. Outer `HResult` and `Source` are not copied to inner
  exceptions. Unsupported or incomplete formats retain the original stack text.
- Nested exceptions remain in one Telegram message and one copyable block. Their
  message and stack budgets are shared. At most five entries are displayed; longer
  trees keep the first four and the last entry, with an explicit omitted count.
  Parsing is bounded to 128K UTF-16 code units, 64 entries and depth 32; inputs beyond these limits
  use the original-text fallback. No nested causes are inferred from plain errors.
- In single-event alerts, valid 16- or 32-digit hexadecimal trace IDs have a `Trace`
  link to the `victoriatraces` Jaeger
  datasource in Grafana Explore, using the configured Grafana Logs URL and log window.
  Missing/invalid Grafana configuration or trace IDs retain the plain trace ID.
  If the final message budget requires dropping links, the trace ID remains as text.
- Small groups include their IDs directly in the URL. Larger groups persist an
  immutable membership snapshot in VictoriaLogs before queueing the notification;
  the link uses `in(subquery)` to select those IDs without an oversized URL. These
  informational records use service `telegram-alert-gateway` (excluded from alerts),
  the same timestamp window and retention as the original logs. Retries preserve
  the membership, and storage failures prevent advancing the polling checkpoint.
  The VictoriaLogs credentials must allow both query and `/insert/jsonline` requests.
- An idempotency key and a PostgreSQL unique constraint suppress webhook retries and
  repeated processing of the same log window.
- Telegram traffic prefers the WireGuard-backed `telegram-vpn` HTTP proxy and falls
  back to direct egress on a proxy transport failure.

Topic and service names use lower-kebab-case. Current topics are:

- `tactical-heroes`;
- `dotnet-template`;
- `postgresql`;
- `core-platform`;
- `observability`;
- `unclassified`;
- `tests`.

Unmatched production alerts use `unclassified`. The `tests` topic is
reserved for synthetic checks with an explicit owner.

## Architecture

The repository follows the PANiXiDA .NET backend template:

- `Domain` contains the notification aggregate and value objects;
- `Application` contains queue commands, handlers, and delivery abstractions;
- `Infrastructure` contains EF Core/PostgreSQL, VictoriaLogs polling, routing,
  rendering, background delivery, and the only `Telegram.Bot` dependency;
- `Presentation` contains the authenticated Alertmanager webhook and health routes;
- `Host` composes the application and OpenTelemetry;
- `Ef.Migrator` applies checked-in migrations before deployment.

The process exposes:

- `/health/live` for Kubernetes liveness;
- `/health/ready` for readiness, including PostgreSQL;
- `/health` for the complete health report.

Runtime logs, traces, ASP.NET Core metrics, HTTP client metrics, PostgreSQL metrics,
.NET runtime metrics, and gateway delivery counters are exported over OTLP.

## Configuration

Production secrets are supplied only through environment variables populated from
OpenBao. Required secret settings are:

- `ConnectionStrings__PostgreSqlConnectionString`;
- `Telegram__BotToken`;
- `Webhook__Token`;
- `VictoriaLogs__Username`;
- `VictoriaLogs__Password`.

Non-secret routing, topic ids, endpoints, polling intervals, and resource limits are
stored in `appsettings.json` and Helm values. Never commit real credentials.

## Local verification

Start Docker Desktop, provide a local PostgreSQL connection string if running the
host, and execute:

```powershell
dotnet restore PANiXiDA.TelegramAlertGateway.slnx
dotnet format PANiXiDA.TelegramAlertGateway.slnx --verify-no-changes --no-restore
dotnet build PANiXiDA.TelegramAlertGateway.slnx --no-restore
dotnet test PANiXiDA.TelegramAlertGateway.slnx --no-build
```

Integration and functional projects use Testcontainers and apply the real EF Core
migration. Helm validation uses the shared
`PANiXiDA-Infrastructure/ci-cd/charts/application` chart.

## Deployment

CI builds `api` and `ef-migrator` images for `main`. Kargo updates
`deploy/helm/telegram-alert-gateway/images-production.yaml`, and Argo CD deploys the
release to the `observability` namespace. Infrastructure configuration, PostgreSQL
provisioning, OpenBao synchronization, Alertmanager routing, and the Argo/Kargo
resources live in `PANiXiDA-Infrastructure/core-platform`.
