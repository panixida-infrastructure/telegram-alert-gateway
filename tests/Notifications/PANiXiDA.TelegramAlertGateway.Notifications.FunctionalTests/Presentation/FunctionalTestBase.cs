using System.Text.Json;

namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

[Collection(FunctionalTestCollection.Name)]
public abstract class FunctionalTestBase(FunctionalTestFixture fixture) : IAsyncLifetime
{
    protected FunctionalTestFixture Fixture { get; } = fixture;

    protected HttpClient Client => Fixture.Client;

    protected static JsonSerializerOptions JsonOptions => TestJsonSerializerOptions.Web;

    public async ValueTask InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }
}
