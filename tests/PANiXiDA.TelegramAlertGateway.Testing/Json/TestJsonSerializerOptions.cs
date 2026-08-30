using System.Text.Json;

namespace PANiXiDA.TelegramAlertGateway.Testing.Json;

public static class TestJsonSerializerOptions
{
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerOptions.Web)
    {
        Converters = { new PaginationResultJsonConverterFactory() }
    };
}
