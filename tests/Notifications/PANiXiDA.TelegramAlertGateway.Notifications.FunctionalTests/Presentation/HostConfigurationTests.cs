using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Host.Common;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

public sealed class HostConfigurationTests(FunctionalTestFixture fixture)
    : FunctionalTestBase(fixture)
{
    [Fact(DisplayName = "Host should configure request body size when application starts")]
    public void Host_Should_ConfigureRequestBodySize_When_ApplicationStarts()
    {
        var options = Fixture.Services
            .GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value;

        options.Limits.MaxRequestBodySize.ShouldBe(FilesConstants.FileRequestSizeLimit);
    }

    [Theory(DisplayName = "Health endpoint should be available when application starts")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task HealthEndpoint_Should_BeAvailable_When_ApplicationStarts(string path)
    {
        using var response = await Fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
