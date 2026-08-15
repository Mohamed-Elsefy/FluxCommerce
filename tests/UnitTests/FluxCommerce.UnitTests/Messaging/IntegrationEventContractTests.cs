using System.Text.Json;
using System.Text.Json.Nodes;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 6.4: the canonical Integration Event contract and metadata model. Verifies the stable event
/// type identifiers, the versioning + serialization contract, metadata defaults/propagation, value
/// equality across the record inheritance, and that the serialized payloads are flat primitives that
/// expose no Domain/EF/Infrastructure types.
/// </summary>
public class IntegrationEventContractTests
{
    private static readonly Guid CorrelationId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---- EventVersion (versioning contract) ----

    [Fact]
    public void EventVersion_SerializesAsMajorMinorString()
    {
        var json = JsonSerializer.Serialize(EventVersion.V1);

        Assert.Equal("\"1.0\"", json);
        Assert.Equal(EventVersion.V1, JsonSerializer.Deserialize<EventVersion>("\"1.0\""));
    }

    [Fact]
    public void EventVersion_RoundTrips_AnyMajorMinor()
    {
        var version = new EventVersion(2, 3);

        var roundTripped = JsonSerializer.Deserialize<EventVersion>(JsonSerializer.Serialize(version));

        Assert.Equal(version, roundTripped);
        Assert.Equal("2.3", roundTripped.ToString());
    }

    [Fact]
    public void EventVersion_InvalidWireValue_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EventVersion>("\"three\""));
    }

    // ---- Canonical metadata model ----

    [Fact]
    public void IntegrationEventMetadata_Defaults_AreCanonical()
    {
        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-1",
            DateTimeOffset.UtcNow)
        {
            CorrelationId = CorrelationId
        };

        Assert.NotEqual(Guid.Empty, @event.EventId);
        Assert.Equal(CorrelationId, @event.CorrelationId);
        Assert.Null(@event.CausationId);
        Assert.Equal(EventVersion.V1, @event.Version);
        Assert.InRange(@event.OccurredAtUtc, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));

        IIntegrationEvent metadata = @event;

        Assert.Equal(@event.EventId, metadata.EventId);
        Assert.Equal(@event.CorrelationId, metadata.CorrelationId);
        Assert.Equal(@event.Version, metadata.Version);
        Assert.Equal(@event.OccurredAtUtc, metadata.OccurredAtUtc);
    }

    // ---- Stable event type identifiers ----

    [Theory]
    [InlineData(typeof(OrderCreatedIntegrationEvent), "orders.order_created")]
    [InlineData(typeof(OrderCancelledIntegrationEvent), "orders.order_cancelled")]
    [InlineData(typeof(InventoryReservationExpiredIntegrationEvent), "inventory.reservation_expired")]
    public void EventType_IsStableIdentifier(Type eventType, string expected)
    {
        var actual = eventType.GetField("EventType")!.GetRawConstantValue();

        Assert.Equal(expected, actual);
    }

    // ---- Serialization / deserialization round-trip (metadata + payload) ----

    [Fact]
    public void OrderCreated_RoundTrips_WithMetadataAndPayload()
    {
        var original = CreateOrderCreated();

        var roundTripped = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void OrderCancelled_RoundTrips_WithMetadataAndPayload()
    {
        var original = CreateOrderCancelled();

        var roundTripped = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void InventoryReservationExpired_RoundTrips_WithMetadataAndPayload()
    {
        var original = CreateInventoryReservationExpired();

        var roundTripped = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
    }

    // ---- Payload boundaries: flat primitives only, no internal types ----

    [Fact]
    public void OrderCreated_Payload_IsFlatPrimitives_NoInternalTypes()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateOrderCreated()))!.AsObject();

        AssertFlatPrimitivePayload(
            json,
            "OrderCreatedIntegrationEvent",
            ["EventId", "CorrelationId", "CausationId", "Version", "OccurredAtUtc", "OrderId", "CustomerId", "OrderNumber", "OccurredAt"]);
    }

    [Fact]
    public void OrderCancelled_Payload_IsFlatPrimitives_NoInternalTypes()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateOrderCancelled()))!.AsObject();

        AssertFlatPrimitivePayload(
            json,
            "OrderCancelledIntegrationEvent",
            ["EventId", "CorrelationId", "CausationId", "Version", "OccurredAtUtc", "OrderId", "CustomerId", "OrderNumber", "OccurredAt"]);
    }

    [Fact]
    public void InventoryReservationExpired_Payload_IsFlatPrimitives_NoInternalTypes()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateInventoryReservationExpired()))!.AsObject();

        AssertFlatPrimitivePayload(
            json,
            "InventoryReservationExpiredIntegrationEvent",
            ["EventId", "CorrelationId", "CausationId", "Version", "OccurredAtUtc", "ReservationId", "OrderId", "Sku", "Quantity", "OccurredAt"]);
    }

    private static void AssertFlatPrimitivePayload(JsonObject json, string contract, string[] expectedProperties)
    {
        foreach (var property in json)
        {
            Assert.False(
                property.Value is JsonObject or JsonArray,
                $"{contract} exposes a nested value for '{property.Key}' — payloads must be flat primitives.");
        }

        Assert.Equal(
            expectedProperties.OrderBy(name => name),
            json.Select(property => property.Key).OrderBy(name => name));

        Assert.Equal("1.0", (string)json["Version"]!);
    }

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-100",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

    private static OrderCancelledIntegrationEvent CreateOrderCancelled() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-200",
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

    private static InventoryReservationExpiredIntegrationEvent CreateInventoryReservationExpired() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-1",
            3,
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };
}