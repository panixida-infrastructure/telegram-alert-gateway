# Telegram Alert Gateway

`telegram-alert-gateway` is the single Telegram delivery boundary for PANiXiDA alerts.
It routes every alert to exactly one owner topic, persists delivery state in PostgreSQL,
and sends messages through `Telegram.Bot`.

## Behavior

- Alertmanager sends metric and health alert webhooks to
  `POST /api/v1/webhooks/alertmanager` with a bearer token.
- Metric alerts preserve `firing` and `resolved` states. Large groups are split into
  multiple Telegram messages; alerts are never silently omitted.
- The gateway polls completed VictoriaLogs windows for error events. Repeated copies
  of one normalized error are combined into one message with an `At least N matching
  events` count and explicit window boundaries. Copies received through multiple
  ingestion paths are not counted twice; different errors remain separate messages.
- A log message includes service, Kubernetes namespace/container, error text,
  exception type, the top of the stack trace, trace id, generic structured fields,
  and a Grafana Logs link narrowed to the source stream and aggregation window when
  those values are present. Values whose field names indicate secrets or credentials
  are redacted before Telegram rendering. Optional sections are reduced before the
  message can exceed Telegram's delivery limit.
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
