using JasperFx;

using PANiXiDA.TelegramAlertGateway.Host.Common;
using PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.DependencyInjection;
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

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPresentation(builder.Configuration);

builder.Host.UseInfrastructure(builder.Configuration);

var app = builder.Build();

app.UsePresentation();

return await app.RunJasperFxCommands(args);
