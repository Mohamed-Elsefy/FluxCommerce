using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxCommerce.BuildingBlocks.Messaging.Versioning;

/// <summary>
/// Serializes <see cref="EventVersion"/> on the wire as its stable <c>"major.minor"</c> string form
/// (matching <see cref="EventVersion.ToString"/> / <see cref="EventVersion.TryParse"/>). This keeps the
/// serialized contract compact and readable instead of the default object shape.
/// </summary>
public sealed class EventVersionJsonConverter : JsonConverter<EventVersion>
{
    public override EventVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        if (EventVersion.TryParse(value, out var version))
        {
            return version;
        }

        throw new JsonException($"'{value}' is not a valid EventVersion. Expected format 'major.minor'.");
    }

    public override void Write(Utf8JsonWriter writer, EventVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}