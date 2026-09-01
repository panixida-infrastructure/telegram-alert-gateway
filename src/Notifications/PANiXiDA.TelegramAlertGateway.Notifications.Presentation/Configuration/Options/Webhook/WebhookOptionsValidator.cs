using Microsoft.Extensions.Options;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Configuration.Options.Webhook;

internal sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Token)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Webhook bearer token is required.");
    }
}
