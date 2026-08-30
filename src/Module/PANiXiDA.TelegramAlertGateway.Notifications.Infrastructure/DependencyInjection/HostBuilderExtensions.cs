using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using PANiXiDA.TelegramAlertGateway.Notifications.Application;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.DependencyInjection;

public static class HostBuilderExtensions
{
    public static IHostBuilder UseInfrastructure(
        this IHostBuilder hostBuilder,
        IConfiguration configuration)
    {
        var messageStoreConnectionString =
            configuration.GetConnectionString(EfConstants.PostgreSqlConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{EfConstants.PostgreSqlConnectionStringName}' was not found.");

        hostBuilder.UseWolverineMediator(
            messageStoreConnectionString,
            modules => modules.AddModule<NotificationsWriteDbContext>(
                ApplicationAssembly.Instance,
                typeof(NotificationsWriteDbContext).Assembly));

        return hostBuilder;
    }
}
