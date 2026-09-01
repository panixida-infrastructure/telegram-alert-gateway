using PANiXiDA.TelegramAlertGateway.Notifications.Application;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Layers;

public sealed class TelegramClientBoundaryTests
{
    [Fact(DisplayName = "Telegram client should be referenced only by infrastructure when assemblies are validated")]
    public void TelegramClient_Should_BeReferencedOnlyByInfrastructure_When_AssembliesAreValidated()
    {
        var outerAssemblies = new[]
        {
            typeof(Notification).Assembly,
            typeof(ApplicationAssembly).Assembly,
            typeof(ServiceCollectionExtensions).Assembly
        };

        foreach (var assembly in outerAssemblies)
        {
            Assert.DoesNotContain(
                "Telegram.Bot",
                assembly.GetReferencedAssemblies().Select(reference => reference.Name));
        }
    }
}
