namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Modules;

public sealed class ModuleDiscoveryTests
{
    [Fact(DisplayName = "Modules should have all expected layer assemblies when discovered")]
    public void Modules_Should_HaveAllExpectedLayerAssemblies_When_Discovered()
    {
        var discoveryErrors = ArchitectureDefinition.ModuleDiscoveryErrors;
        var modules = ArchitectureDefinition.Modules;

        Assert.Empty(discoveryErrors);
        Assert.NotEmpty(modules);
    }
}
