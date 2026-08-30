using Asp.Versioning;

using Microsoft.AspNetCore.Routing;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Presentation.Features.Webhooks;

internal sealed class WebhooksEndpoints : IEndpointGroup
{
    public string Route { get; } = "webhooks";
    public string Name { get; } = "Webhooks";
    public ApiVersion ApiVersion { get; } = new(1, 0);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        EndpointMapper.MapGroupEndpoints<WebhooksEndpoints>(endpoints);
    }
}
