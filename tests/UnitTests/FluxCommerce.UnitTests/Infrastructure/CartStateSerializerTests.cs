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
