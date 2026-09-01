using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.DependencyInjection;
using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

namespace PANiXiDA.TelegramAlertGateway.Host.Configurations.Modules;

internal static class NotificationsModuleConfiguration
{
    internal static WebApplicationBuilder AddNotificationsModule(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddPresentation(builder.Configuration);
        builder.Host.UseInfrastructure(builder.Configuration);

        return builder;
    }
}
