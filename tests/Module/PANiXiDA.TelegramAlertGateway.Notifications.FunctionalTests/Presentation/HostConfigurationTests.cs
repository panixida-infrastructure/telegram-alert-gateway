using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Host.Common;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

public sealed class HostConfigurationTests(FunctionalTestFixture fixture)
    : FunctionalTestBase(fixture)
{
    [Fact(DisplayName = "Host should configure request body size when application starts")]
    public void Host_Should_Configure_Request_Body_Size_When_Application_Starts()
    {
        var options = Fixture.Services
            .GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value;

        options.Limits.MaxRequestBodySize.ShouldBe(FilesConstants.FileRequestSizeLimit);
    }

    [Theory(DisplayName = "Health endpoints should be available")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Health_Endpoint_Should_Be_Available(string path)
    {
        using var response = await Fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
