using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PANiXiDA.Core.Application.Messaging.Mediator;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.QueueLogEvents;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;
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
                        TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                        timeProvider,
                        stoppingToken);
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
                    TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                    timeProvider,
                    stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextWindowAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsWriteDbContext>();
        var now = timeProvider.GetUtcNow();
        var latestCompleteWindowEnd = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            now.Hour,
            now.Minute,
            0,
            TimeSpan.Zero);

        if (now < latestCompleteWindowEnd.AddSeconds(_options.IngestionDelaySeconds))
        {
            latestCompleteWindowEnd = latestCompleteWindowEnd.AddMinutes(-1);
        }

        var checkpoint = await dbContext.LogIngestionCheckpoints
            .SingleOrDefaultAsync(item => item.Id == CheckpointId, cancellationToken);
        var windowStart = checkpoint?.NextWindowStartUtc
            ?? latestCompleteWindowEnd.AddMinutes(-1);
        var windowEnd = windowStart.AddMinutes(1);

        if (windowEnd > latestCompleteWindowEnd)
        {
            return false;
        }

        var client = scope.ServiceProvider.GetRequiredService<VictoriaLogsClient>();
        var normalizer = scope.ServiceProvider.GetRequiredService<LogEventNormalizer>();
        var records = await client.QueryAsync(windowStart, windowEnd, cancellationToken);
        var events = normalizer.Normalize(records);
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.SendAsync(
            new QueueLogEventsCommand(windowStart, events, now),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Log notification queueing failed: {string.Join("; ", result.Errors)}");
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
