using System.Text.Json;

namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed class CartStateSerializer
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record CartEnvelope(int SchemaVersion, CartDocument? Data);

    public string Serialize(CartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var envelope = new CartEnvelope(SchemaVersion, document);

        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public CartDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Cart state document is empty.");

        var envelope = JsonSerializer.Deserialize<CartEnvelope>(json, SerializerOptions)
            ?? throw new InvalidDataException("Cart state document is empty.");

        if (envelope.SchemaVersion != SchemaVersion)
            throw new InvalidDataException(
                $"Unsupported Cart state schema version '{envelope.SchemaVersion}'. Expected '{SchemaVersion}'.");

        return envelope.Data
            ?? throw new InvalidDataException("Cart state document data is missing.");
    }
}
