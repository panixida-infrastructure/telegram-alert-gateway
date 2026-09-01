using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Composition;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.AlertRouting;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.NotificationRetention;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.Telegram;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.VictoriaLogs;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Health;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Features.Notifications.Write;
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

        serviceCollection.AddSingleton<
            IValidateOptions<TelegramOptions>,
            TelegramOptionsValidator>();
        serviceCollection.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();
        serviceCollection.AddSingleton<
            IValidateOptions<AlertRoutingOptions>,
            AlertRoutingOptionsValidator>();
        serviceCollection.AddOptions<AlertRoutingOptions>()
            .Bind(configuration.GetSection(AlertRoutingOptions.SectionName))
            .ValidateOnStart();
        serviceCollection.AddSingleton<
            IValidateOptions<VictoriaLogsOptions>,
            VictoriaLogsOptionsValidator>();
        serviceCollection.AddOptions<VictoriaLogsOptions>()
            .Bind(configuration.GetSection(VictoriaLogsOptions.SectionName))
            .ValidateOnStart();
        serviceCollection.AddSingleton<
            IValidateOptions<NotificationRetentionOptions>,
            NotificationRetentionOptionsValidator>();
        serviceCollection.AddOptions<NotificationRetentionOptions>()
            .Bind(configuration.GetSection(NotificationRetentionOptions.SectionName))
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

        if (configuration.GetValue(
                key: "BackgroundProcessing:Enabled",
                defaultValue: true))
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
