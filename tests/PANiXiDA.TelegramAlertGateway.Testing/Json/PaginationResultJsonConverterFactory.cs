using System.Text.Json;
using System.Text.Json.Serialization;

namespace PANiXiDA.TelegramAlertGateway.Testing.Json;

public sealed class PaginationResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType &&
               typeToConvert.GetGenericTypeDefinition() == typeof(PaginationResult<>);
    }

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var itemType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(PaginationResultJsonConverter<>).MakeGenericType(itemType);

        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class PaginationResultJsonConverter<TItem>
        : JsonConverter<PaginationResult<TItem>>
    {
        public override PaginationResult<TItem> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var items = root.GetProperty("items").Deserialize<List<TItem>>(options) ?? [];
            var pageNumber = root.GetProperty("pageNumber").GetInt32();
            var pageSize = root.GetProperty("pageSize").GetInt32();
            var totalCount = root.GetProperty("totalCount").GetInt64();

            return PaginationResult<TItem>.Create(
                items,
                pageNumber,
                pageSize,
                totalCount);
        }

        public override void Write(
            Utf8JsonWriter writer,
            PaginationResult<TItem> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("items");
            JsonSerializer.Serialize(writer, value.Items, options);
            writer.WriteNumber("pageNumber", value.PageNumber);
            writer.WriteNumber("pageSize", value.PageSize);
            writer.WriteNumber("totalCount", value.TotalCount);
            writer.WriteNumber("totalPages", value.TotalPages);
            writer.WriteBoolean("hasPreviousPage", value.HasPreviousPage);
            writer.WriteBoolean("hasNextPage", value.HasNextPage);
            writer.WriteEndObject();
        }
    }
}
