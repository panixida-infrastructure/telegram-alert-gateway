namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests;

public sealed class FunctionalTestAssemblyTests
{
    [Fact(DisplayName = "Functional test assembly should load when discovered")]
    public void FunctionalTestAssembly_Should_Load_When_Discovered()
    {
        var assembly = typeof(FunctionalTestAssemblyTests).Assembly;

        var assemblyName = assembly.GetName().Name;

        assemblyName.ShouldBe("PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests");
    }
}
