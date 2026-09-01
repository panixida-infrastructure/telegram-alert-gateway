using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Configuration.Options.Webhook;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection.AddHttp(configuration);
        serviceCollection.AddSingleton<
            IValidateOptions<WebhookOptions>,
            WebhookOptionsValidator>();
        serviceCollection.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .ValidateOnStart();

        return serviceCollection;
    }
}
