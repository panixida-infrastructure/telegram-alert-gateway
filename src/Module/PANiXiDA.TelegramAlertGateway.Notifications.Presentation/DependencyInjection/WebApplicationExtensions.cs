using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.DependencyInjection;

public static class WebApplicationExtensions
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        app.UseHttp(Assembly.GetExecutingAssembly());
        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready")
            });
        return app;
    }
}
