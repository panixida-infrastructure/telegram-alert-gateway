using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PANiXiDA.Core.Application.Messaging.Mediator;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal sealed class LogPollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<VictoriaLogsOptions> options,
    TimeProvider timeProvider,
    ILogger<LogPollingWorker> logger) : BackgroundService
{
    private const string CheckpointId = "victoria-logs-errors";
    private readonly VictoriaLogsOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextWindowAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(
                        delay: TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                        timeProvider: timeProvider,
                        cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "VictoriaLogs polling failed.");
                await Task.Delay(
                    delay: TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                    timeProvider: timeProvider,
                    cancellationToken: stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextWindowAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var now = timeProvider.GetUtcNow();
        var windowSize = TimeSpan.FromSeconds(_options.WindowSeconds);
        var ingestionCutoff = now.AddSeconds(-_options.IngestionDelaySeconds);
        var latestCompleteWindowEnd = new DateTimeOffset(
            ingestionCutoff.UtcTicks / windowSize.Ticks * windowSize.Ticks,
            TimeSpan.Zero);

        var checkpoint = await dbContext.LogIngestionCheckpoints
            .SingleOrDefaultAsync(item => item.Id == CheckpointId, cancellationToken);
        var windowStart = checkpoint?.NextWindowStartUtc
            ?? latestCompleteWindowEnd.Subtract(windowSize);
        var windowEnd = windowStart.Add(windowSize);

        if (windowEnd > latestCompleteWindowEnd)
        {
            return false;
        }

        var client = scope.ServiceProvider.GetRequiredService<VictoriaLogsClient>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = await client.QueryAsync(
            startUtc: windowStart,
            endUtc: windowEnd,
            cancellationToken: cancellationToken);
        var events = normalizer.Normalize(records);
        if (events.Count > 0)
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.SendAsync(
                command: new QueueLogEventsCommand(
                    WindowStartUtc: windowStart,
                    Events: events,
                    ReceivedAtUtc: now),
                cancellationToken: cancellationToken);

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Log notification queueing failed: {string.Join("; ", result.Errors)}");
            }
        }

        if (checkpoint is null)
        {
            checkpoint = new LogIngestionCheckpoint(CheckpointId, windowEnd);
            dbContext.LogIngestionCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.Advance(windowEnd, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Processed VictoriaLogs window {WindowStart} with {LogCount} records and {EventCount} error groups.",
            windowStart,
            records.Count,
            events.Count);

        return true;
    }
}
