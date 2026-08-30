using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;

internal sealed class TopicRouter(IOptions<AlertRoutingOptions> options) : ITopicRouter
{
    private readonly AlertRoutingOptions _options = options.Value;

    public string Route(IReadOnlyDictionary<string, string> dimensions)
    {
        var owner = GetDimension(dimensions, "alert_owner", "owner");
        var searchText = string.Join(
            ' ',
            dimensions.Values.Where(value => !string.IsNullOrWhiteSpace(value)));

        foreach (var rule in _options.Rules)
        {
            if (string.Equals(owner, rule.Topic, StringComparison.OrdinalIgnoreCase)
                || rule.Matches.Any(match =>
                    searchText.Contains(match, StringComparison.OrdinalIgnoreCase)))
            {
                return rule.Topic;
            }
        }

        return _options.DefaultTopic;
    }

    private static string? GetDimension(
        IReadOnlyDictionary<string, string> dimensions,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (dimensions.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
