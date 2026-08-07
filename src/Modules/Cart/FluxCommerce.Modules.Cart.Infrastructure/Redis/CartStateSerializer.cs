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

        CartEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CartEnvelope>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Cart state document is not valid JSON.", exception);
        }

        if (envelope is null)
            throw new InvalidDataException("Cart state document is empty.");

        if (envelope.SchemaVersion != SchemaVersion)
            throw new InvalidDataException(
                $"Unsupported Cart state schema version '{envelope.SchemaVersion}'. Expected '{SchemaVersion}'.");

        if (envelope.Data is null)
            throw new InvalidDataException("Cart state document data is missing.");

        ValidateDocument(envelope.Data);

        return envelope.Data;
    }

    private static void ValidateDocument(CartDocument document)
    {
        if (document.CartId == Guid.Empty)
            throw new InvalidDataException("Cart state document has an empty cart id.");

        if (document.Items is null)
            throw new InvalidDataException("Cart state document has no items collection.");

        for (var index = 0; index < document.Items.Count; index++)
            ValidateItem(document.Items[index], index);
    }

    private static void ValidateItem(CartItemDocument item, int index)
    {
        if (item.ProductId == Guid.Empty)
            throw new InvalidDataException($"Cart state item at index {index} has an empty product id.");

        if (item.VariantId == Guid.Empty)
            throw new InvalidDataException($"Cart state item at index {index} has an empty variant id.");

        if (string.IsNullOrWhiteSpace(item.Sku))
            throw new InvalidDataException($"Cart state item at index {index} has an empty SKU.");

        if (string.IsNullOrWhiteSpace(item.ProductName))
            throw new InvalidDataException($"Cart state item at index {index} has an empty product name.");

        if (item.Quantity < 1)
            throw new InvalidDataException($"Cart state item at index {index} has an invalid quantity.");

        if (item.UnitPrice < 0)
            throw new InvalidDataException($"Cart state item at index {index} has a negative unit price.");

        if (string.IsNullOrWhiteSpace(item.Currency))
            throw new InvalidDataException($"Cart state item at index {index} has an empty currency.");
    }
}
