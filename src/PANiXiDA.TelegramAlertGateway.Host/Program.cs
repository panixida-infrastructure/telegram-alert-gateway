using JasperFx;

using PANiXiDA.TelegramAlertGateway.Host.Common;
using PANiXiDA.TelegramAlertGateway.Host.Configurations.Modules;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Telemetry;
using PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(GatewayTelemetry.MeterName));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = FilesConstants.FileRequestSizeLimit;
});

builder.AddNotificationsModule();

var app = builder.Build();

app.UsePresentation();

return await app.RunJasperFxCommands(args);
