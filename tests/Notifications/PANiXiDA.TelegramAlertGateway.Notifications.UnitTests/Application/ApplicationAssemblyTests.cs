using PANiXiDA.TelegramAlertGateway.Notifications.Application;

namespace PANiXiDA.TelegramAlertGateway.Notifications.UnitTests.Application;

public sealed class ApplicationAssemblyTests
{
    [Fact(DisplayName = "Instance should return application assembly when accessed")]
    public void Instance_Should_ReturnApplicationAssembly_When_Accessed()
    {
        var expectedAssembly = typeof(ApplicationAssembly).Assembly;

        var assembly = ApplicationAssembly.Instance;

        assembly.ShouldBe(expectedAssembly);
    }
}
