using System.Text.Json;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CartStateSerializerTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Serialize_EmitsSchemaVersionEnvelope()
    {
        var serializer = new CartStateSerializer();

        var json = serializer.Serialize(CreateDocument());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(CartStateSerializer.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.TryGetProperty("data", out _));
    }

    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var serializer = new CartStateSerializer();

        var json = serializer.Serialize(CreateDocument());

        Assert.Contains("\"cartId\"", json);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"createdAtUtc\"", json);
        Assert.Contains("\"updatedAtUtc\"", json);
        Assert.Contains("\"items\"", json);
        Assert.Contains("\"productId\"", json);
        Assert.Contains("\"variantId\"", json);
        Assert.Contains("\"sku\"", json);
        Assert.Contains("\"productName\"", json);
        Assert.Contains("\"unitPrice\"", json);
        Assert.Contains("\"currency\"", json);
        Assert.Contains("\"quantity\"", json);
        Assert.DoesNotContain("\"CartId\"", json);
    }

    [Fact]
    public void Serialize_DoesNotLeakVersionIntoEnvelopeRoot()
    {
        var serializer = new CartStateSerializer();

        var json = serializer.Serialize(CreateDocument());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = root.GetProperty("data");

        var rootProperties = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["schemaVersion", "data"], rootProperties);
        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.False(root.TryGetProperty("version", out _));
        Assert.True(data.TryGetProperty("version", out _));
    }

    [Fact]
    public void Serialize_DoesNotEmbedExpirationInPayload()
    {
        var serializer = new CartStateSerializer();

        var json = serializer.Serialize(CreateDocument());

        Assert.DoesNotContain("expiresAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ttl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expiry", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_RoundTripsDocument()
    {
        var serializer = new CartStateSerializer();
        var original = CreateDocument();

        var roundTripped = serializer.Deserialize(serializer.Serialize(original));

        Assert.Equal(original.CartId, roundTripped.CartId);
        Assert.Equal(original.UserId, roundTripped.UserId);
        Assert.Equal(original.Version, roundTripped.Version);
        Assert.Equal(original.CreatedAtUtc, roundTripped.CreatedAtUtc);
        Assert.Equal(original.UpdatedAtUtc, roundTripped.UpdatedAtUtc);
        Assert.Equal(original.Items.Count, roundTripped.Items.Count);
        Assert.Equal(original.Items[0], roundTripped.Items[0]);
    }

    [Fact]
    public void Serialize_NullDocument_Throws()
    {
        var serializer = new CartStateSerializer();

        Assert.Throws<ArgumentNullException>(() => serializer.Serialize(null!));
    }

    [Fact]
    public void Deserialize_NullJson_Throws()
    {
        var serializer = new CartStateSerializer();

        Assert.Throws<ArgumentNullException>(() => serializer.Deserialize(null!));
    }

    [Fact]
    public void Deserialize_EmptyJson_Throws()
    {
        var serializer = new CartStateSerializer();

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(string.Empty));
    }

    [Fact]
    public void Deserialize_UnsupportedSchemaVersion_Throws()
    {
        var serializer = new CartStateSerializer();
        var json = """{"schemaVersion":2,"data":{}}""";

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_MissingData_Throws()
    {
        var serializer = new CartStateSerializer();
        var json = """{"schemaVersion":1}""";

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws()
    {
        var serializer = new CartStateSerializer();
        var json = """{"schemaVersion":1,"data":{"cartId":""";

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyCartId_Throws()
    {
        var serializer = new CartStateSerializer();
        var json = serializer.Serialize(CreateDocument() with { CartId = Guid.Empty });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_NullItems_Throws()
    {
        var serializer = new CartStateSerializer();
        var json = serializer.Serialize(CreateDocument() with { Items = null! });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deserialize_InvalidQuantity_Throws(int quantity)
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { Quantity = quantity };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_NegativeUnitPrice_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { UnitPrice = -1m };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptySku_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { Sku = string.Empty };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyProductName_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { ProductName = string.Empty };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyCurrency_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { Currency = string.Empty };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyVariantId_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { VariantId = Guid.Empty };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyProductId_Throws()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { ProductId = Guid.Empty };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyItems_IsValid()
    {
        var serializer = new CartStateSerializer();
        var original = CreateDocument() with { Items = [] };

        var roundTripped = serializer.Deserialize(serializer.Serialize(original));

        Assert.Empty(roundTripped.Items);
    }

    [Fact]
    public void Serialize_RoundTripsGuestDocument()
    {
        var serializer = new CartStateSerializer();
        var original = CreateDocument() with { UserId = null };

        var roundTripped = serializer.Deserialize(serializer.Serialize(original));

        Assert.Null(roundTripped.UserId);
    }

    [Fact]
    public void Serialize_UsesIso8601DateTimeOffsets()
    {
        var serializer = new CartStateSerializer();

        var json = serializer.Serialize(CreateDocument());

        Assert.Contains("\"2026-08-07T10:30:00+00:00\"", json);
        Assert.Contains("\"2026-08-07T10:35:00+00:00\"", json);
    }

    [Fact]
    public void Serialize_PreservesMonetaryPrecision()
    {
        var serializer = new CartStateSerializer();
        var item = CreateDocument().Items[0] with { UnitPrice = 19.9900m };
        var json = serializer.Serialize(CreateDocument() with { Items = [item] });

        var roundTripped = serializer.Deserialize(json);

        Assert.Equal(19.9900m, roundTripped.Items[0].UnitPrice);
    }

    private static CartDocument CreateDocument() =>
        new(
            CartId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Version: 7,
            CreatedAtUtc: FixedTime,
            UpdatedAtUtc: FixedTime.AddMinutes(5),
            Items:
            [
                new CartItemDocument(
                    ProductId: Guid.NewGuid(),
                    VariantId: Guid.NewGuid(),
                    Sku: "SKU-1001",
                    ProductName: "Wireless Mouse",
                    UnitPrice: 24.99m,
                    Currency: "USD",
                    Quantity: 2),
            ]);
}
