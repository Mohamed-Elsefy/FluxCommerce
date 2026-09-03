using System.Text.Json;
using System.Text.Json.Nodes;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 11.2: the reservation-expiry integration contract. Pins the required metadata (EventId,
/// CorrelationId, CausationId, Version, OccurredAtUtc) and domain payload (ReservationId, OrderId,
/// Sku, Quantity, ExpiredAtUtc), the stable EventType identifier, JSON round-trip fidelity, and the
/// flat-primitive wire shape (no Domain/EF/Infrastructure types leak across the async boundary).
/// </summary>
public class InventoryReservationExpiredIntegrationEventTests
{
    private static readonly Guid CorrelationId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Requires_CanonicalMetadata_And_DomainPayload()
    {
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var expiredAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var @event = new InventoryReservationExpiredIntegrationEvent(
            reservationId,
            orderId,
            "SKU-1",
            3,
            expiredAt)
        {
            CorrelationId = CorrelationId
        };

        IIntegrationEvent metadata = @event;

        Assert.Equal(reservationId, @event.ReservationId);
        Assert.Equal(orderId, @event.OrderId);
        Assert.Equal("SKU-1", @event.Sku);
        Assert.Equal(3, @event.Quantity);
        Assert.Equal(expiredAt, @event.ExpiredAtUtc);

        Assert.NotEqual(Guid.Empty, @event.EventId);
        Assert.Equal(CorrelationId, metadata.CorrelationId);
        Assert.Null(metadata.CausationId);
        Assert.Equal(EventVersion.V1, metadata.Version);
        Assert.InRange(metadata.OccurredAtUtc, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Payload_IsFlatPrimitives_WithCanonicalMetadata()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(CreateEvent()))!.AsObject();

        foreach (var property in json)
        {
            Assert.False(
                property.Value is JsonObject or JsonArray,
                $"exposes a nested value for '{property.Key}' — the payload must stay flat.");
        }

        Assert.Equal(
            new[]
            {
                "EventId", "CorrelationId", "CausationId", "Version", "OccurredAtUtc",
                "ReservationId", "OrderId", "Sku", "Quantity", "ExpiredAtUtc"
            }.OrderBy(name => name),
            json.Select(property => property.Key).OrderBy(name => name));

        Assert.Equal("1.0", (string)json["Version"]!);
    }

    [Fact]
    public void RoundTrips_WithMetadataAndPayload()
    {
        var original = CreateEvent();

        var roundTripped = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
        Assert.Equal(original.ExpiredAtUtc, roundTripped!.ExpiredAtUtc);
        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
    }

    [Fact]
    public void EventType_IsStableIdentifier_AndVersionDefaultsToV1()
    {
        Assert.Equal("inventory.reservation_expired", InventoryReservationExpiredIntegrationEvent.EventType);
        Assert.Equal(EventVersion.V1, CreateEvent().Version);
    }

    private static InventoryReservationExpiredIntegrationEvent CreateEvent() =>
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
