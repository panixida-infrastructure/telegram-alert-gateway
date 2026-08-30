using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
       .AddEnvironmentVariables();
    })
        .ConfigureServices((ctx, services) =>
        {
            services.AddPostgreSqlEfRepository<
                NotificationsWriteDbContext, NotificationsReadDbContext>(ctx.Configuration);
        })
    .Build();

await host.RunMigrationsAsync<NotificationsWriteDbContext>();
