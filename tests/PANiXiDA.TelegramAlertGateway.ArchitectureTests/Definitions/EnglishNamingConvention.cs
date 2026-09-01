using Humanizer;

namespace PANiXiDA.TelegramAlertGateway.ArchitectureTests.Definitions;

internal static class EnglishNamingConvention
{
    internal static string Pluralize(string singularName)
    {
        return singularName.Pluralize(inputIsKnownToBeSingular: true);
    }
}
