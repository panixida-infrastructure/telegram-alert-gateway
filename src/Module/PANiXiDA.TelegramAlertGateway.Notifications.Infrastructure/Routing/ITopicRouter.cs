namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Routing;

internal interface ITopicRouter
{
    string Route(IReadOnlyDictionary<string, string> dimensions);
}
