namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

public sealed class LogIngestionCheckpoint
{
    private LogIngestionCheckpoint()
    {
    }

    public LogIngestionCheckpoint(string id, DateTimeOffset nextWindowStartUtc)
    {
        Id = id;
        NextWindowStartUtc = nextWindowStartUtc;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Id { get; private set; } = string.Empty;
    public DateTimeOffset NextWindowStartUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Advance(DateTimeOffset nextWindowStartUtc, DateTimeOffset now)
    {
        NextWindowStartUtc = nextWindowStartUtc;
        UpdatedAtUtc = now;
    }
}
