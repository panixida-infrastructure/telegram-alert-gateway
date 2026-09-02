using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Wolverine;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

internal sealed class FunctionalTestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private static readonly TimeProvider FixedTimeProvider = new TestTimeProvider(
        new DateTimeOffset(2026, 8, 30, 10, 1, 0, TimeSpan.Zero));

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
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(FixedTimeProvider);

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

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
