namespace PANiXiDA.TelegramAlertGateway.Notifications.FunctionalTests.Presentation;

[CollectionDefinition(Name)]
public sealed class FunctionalTestCollection : ICollectionFixture<FunctionalTestFixture>
{
    public const string Name = "Functional";
}
