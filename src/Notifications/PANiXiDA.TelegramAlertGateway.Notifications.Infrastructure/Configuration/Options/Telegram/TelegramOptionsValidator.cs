using Microsoft.Extensions.Options;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Configuration.Options.Telegram;

internal sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        var isValid = !string.IsNullOrWhiteSpace(options.BotToken)
                      && options.ChatId != 0
                      && options.Topics.Count > 0
                      && options.Topics.Keys.All(topic =>
                          TopicName.Create(topic).IsSuccess);

        return isValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Telegram bot token, chat id, and lower-kebab-case topics are required.");
    }
}
