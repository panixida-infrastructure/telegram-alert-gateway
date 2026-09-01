using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Health;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telegram;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection.AddPostgreSqlEfRepository<
            NotificationsWriteDbContext, NotificationsReadDbContext>(configuration);

        serviceCollection.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.BotToken)
                    && options.ChatId != 0
                    && options.Topics.Count > 0
                    && options.Topics.Keys.All(topic => TopicName.Create(topic).IsSuccess),
                "Telegram bot token, chat id, and lower-kebab-case topics are required.")
            .ValidateOnStart();
        serviceCollection.AddOptions<AlertRoutingOptions>()
            .Bind(configuration.GetSection(AlertRoutingOptions.SectionName))
            .Validate(
                options => TopicName.Create(options.DefaultTopic).IsSuccess
                    && options.Rules.All(rule =>
                        TopicName.Create(rule.Topic).IsSuccess
                        && rule.Matches.Count > 0
                        && rule.Matches.All(match => !string.IsNullOrWhiteSpace(match))),
                "Alert routes must use lower-kebab-case topics and non-empty matches.")
            .ValidateOnStart();
        serviceCollection.AddOptions<VictoriaLogsOptions>()
            .Bind(configuration.GetSection(VictoriaLogsOptions.SectionName))
            .Validate(
                options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _)
                    && !string.IsNullOrWhiteSpace(options.Query)
                    && options.PollIntervalSeconds > 0
                    && options.WindowSeconds > 0
                    && options.IngestionDelaySeconds >= 0
                    && options.MaxEntriesPerWindow > 0,
                "VictoriaLogs endpoint, query, and positive polling limits are required.")
            .ValidateOnStart();
        serviceCollection.AddOptions<NotificationRetentionOptions>()
            .Bind(configuration.GetSection(NotificationRetentionOptions.SectionName))
            .Validate(
                options => options.SentRetentionDays > 0
                    && options.FailedRetentionDays >= options.SentRetentionDays
                    && options.BatchSize is > 0 and <= 10000
                    && options.CleanupIntervalHours is > 0 and <= 168,
                "Notification retention periods, batch size, and cleanup interval are invalid.")
            .ValidateOnStart();

        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<ITopicRouter, TopicRouter>();
        serviceCollection.AddSingleton<INotificationComposer, TelegramNotificationComposer>();
        serviceCollection.AddScoped<INotificationsRepository, NotificationsRepository>();
        serviceCollection.AddScoped<NotificationRetentionCleaner>();
        serviceCollection.AddScoped<ITelegramNotificationSender, TelegramNotificationSender>();
        serviceCollection.AddSingleton<TelegramClientFactory>();
        serviceCollection.AddScoped<LogEventNormalizer>();

        serviceCollection.AddHttpClient<VictoriaLogsClient>((provider, client) =>
        {
            var options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<VictoriaLogsOptions>>()
                .Value;
            client.BaseAddress = new Uri(options.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(45);
        });

        serviceCollection.AddHttpClient(TelegramClientFactory.DirectClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        var proxyUrl = configuration[$"{TelegramOptions.SectionName}:ProxyUrl"];
        serviceCollection
            .AddHttpClient(TelegramClientFactory.ProxiedClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = string.IsNullOrWhiteSpace(proxyUrl) ? null : new WebProxy(proxyUrl),
                UseProxy = !string.IsNullOrWhiteSpace(proxyUrl),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (configuration.GetValue("BackgroundProcessing:Enabled", true))
        {
            serviceCollection.AddHostedService<LogPollingWorker>();
            serviceCollection.AddHostedService<NotificationDeliveryWorker>();
            serviceCollection.AddHostedService<NotificationRetentionWorker>();
        }
        serviceCollection.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);

        return serviceCollection;
    }
}
