namespace PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests;

public sealed class IntegrationTestAssemblyTests
{
    [Fact(DisplayName = "Integration test assembly should load when discovered")]
    public void IntegrationTestAssembly_Should_Load_When_Discovered()
    {
        var assembly = typeof(IntegrationTestAssemblyTests).Assembly;

        var assemblyName = assembly.GetName().Name;

        assemblyName.ShouldBe("PANiXiDA.TelegramAlertGateway.Notifications.IntegrationTests");
    }
}
