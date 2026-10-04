using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Host.Common;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Abstractions;
using PANiXiDA.TelegramAlertGateway.Notifications.Application.Notifications.Models;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

public sealed class HostConfigurationTests(FunctionalTestFixture fixture)
    : FunctionalTestBase(fixture)
{
    [Theory(DisplayName = "Host should route logs when service namespace and owner are provided")]
    [InlineData("sonarqube", "quality", null, "core-platform")]
    [InlineData("sonarqube", "quality", "tests", "tests")]
    [InlineData("other-quality-service", "quality", null, "unclassified")]
    [InlineData("envoy", "envoy-gateway-system", null, "core-platform")]
    [InlineData("envoy-gateway", "envoy-gateway-system", null, "core-platform")]
    [InlineData("envoy", "envoy-gateway-system", "tests", "tests")]
    public void Host_Should_RouteLogs_When_ServiceNamespaceAndOwnerAreProvided(
        string service,
        string namespaceName,
        string? owner,
        string expectedTopic)
    {
        using var scope = Fixture.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<INotificationComposer>();
        var logEvent = new LogEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Service: service,
            Namespace: namespaceName,
            Container: service,
            Owner: owner,
            Severity: "error",
            Message: "An unexpected error occurred.",
            ExceptionType: null,
            StackTrace: null,
            TraceId: null,
            Fields: new Dictionary<string, string>(),
            Fingerprint: "routing-error",
            Occurrences: 1);

        var notification = composer.ComposeLogEvent(logEvent.Timestamp, logEvent);

        notification.Topic.ShouldBe(expectedTopic);
    }

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
