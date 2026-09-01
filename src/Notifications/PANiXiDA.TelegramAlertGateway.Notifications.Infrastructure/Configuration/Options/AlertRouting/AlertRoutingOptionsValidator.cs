using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.AlertRouting;

internal sealed class AlertRoutingOptionsValidator : IValidateOptions<AlertRoutingOptions>
{
    public ValidateOptionsResult Validate(string? name, AlertRoutingOptions options)
    {
        var isValid = TopicName.Create(options.DefaultTopic).IsSuccess
                      && options.Rules.All(rule =>
                          TopicName.Create(rule.Topic).IsSuccess
                          && rule.Matches.Count > 0
                          && rule.Matches.All(match =>
                              !string.IsNullOrWhiteSpace(match)));

        return isValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Alert routes must use lower-kebab-case topics and non-empty matches.");
    }
}
