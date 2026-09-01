using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Wolverine;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

internal sealed class FunctionalTestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundProcessing:Enabled"] = "false",
                ["Webhook:Token"] = "test-webhook-token",
                ["Telegram:BotToken"] = "test-bot-token"
            });
        });
        builder.ConfigureServices(services =>
        {
            var gatewayWorkers = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace?.StartsWith(
                        "PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure",
                        StringComparison.Ordinal) == true)
                .ToArray();

            foreach (var worker in gatewayWorkers)
            {
                services.Remove(worker);
            }

            services.RunWolverineInSoloMode();
        });
    }
}
