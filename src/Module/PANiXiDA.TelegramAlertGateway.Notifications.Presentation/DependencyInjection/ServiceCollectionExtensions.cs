using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Configuration;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        serviceCollection.AddHttp(configuration);
        serviceCollection.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Token),
                "Webhook bearer token is required.")
            .ValidateOnStart();

        return serviceCollection;
    }
}
