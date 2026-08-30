namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Modules;

public sealed class ModuleDiscoveryTests
{
    [Fact(DisplayName = "Modules should have all expected layer assemblies")]
    public void Modules_Should_Have_All_Expected_Layer_Assemblies()
    {
        Assert.Empty(ArchitectureDefinition.ModuleDiscoveryErrors);
        Assert.NotEmpty(ArchitectureDefinition.Modules);
    }
}
