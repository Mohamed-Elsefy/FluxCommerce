using System.Text.Json;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.3: expiring a reservation on the real SQL Server engine persists the
/// InventoryReservationExpired outbox record in the same transaction as the reservation state change
/// — Status Pending, correct type and payload, and exactly one record per expiry.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryOutboxIntegrationTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryOutboxIntegrationTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExpiringReservation_PersistsOutbox_Atomically()
    {
        var sku = UniqueSku("OUT-EXP");
        var reservationId = await _fixture.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 3,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            await context.OutboxMessages
                .Where(message => message.Type == InventoryReservationExpiredIntegrationEvent.EventType)
                .ExecuteDeleteAsync();
        }

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.True(expired >= 1);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var reservation = (await _fixture.GetAllReservationsAsync())
            .Single(candidate => candidate.Id.Value == reservationId);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);

        var matching = await FindMessagesAsync(verifyContext, reservationId);

        var message = Assert.Single(matching);

        Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, message.Type);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.CausationId);
        Assert.NotEqual(Guid.Empty, message.CorrelationId);

        var payload = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(message.Payload);

        Assert.NotNull(payload);
        Assert.Equal(reservationId, payload!.ReservationId);
        Assert.Equal(sku.ToUpperInvariant(), payload.Sku);
        Assert.Equal(3, payload.Quantity);

        await verifyContext.OutboxMessages
            .Where(outbox => outbox.Id == message.Id)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ExpiringReservation_OutboxPayload_IsAccurate_IncludingOrderAndExpiry()
    {
        var sku = UniqueSku("OUT-FULL");
        var reservationId = await _fixture.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 3,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var seededOrderId = (await _fixture.GetAllReservationsAsync())
            .Single(candidate => candidate.Id.Value == reservationId)
            .OrderId
            .Value;

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.OutboxMessages
                .Where(message => message.Type == InventoryReservationExpiredIntegrationEvent.EventType)
                .ExecuteDeleteAsync();
        }

        Assert.True(await _fixture.RunExpirationProcessorAsync(maxResults: 100) >= 1);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var message = Assert.Single(await FindMessagesAsync(verifyContext, reservationId));

        var payload = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(message.Payload);

        Assert.NotNull(payload);
        Assert.Equal(reservationId, payload!.ReservationId);
        Assert.Equal(seededOrderId, payload.OrderId);
        Assert.Equal(sku.ToUpperInvariant(), payload.Sku);
        Assert.Equal(3, payload.Quantity);
        Assert.Equal(message.CorrelationId, payload.CorrelationId);
        Assert.Null(payload.CausationId);
        Assert.InRange(
            payload.ExpiredAtUtc,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(1));

        await verifyContext.OutboxMessages
            .Where(outbox => outbox.Id == message.Id)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RepeatedExpirationRun_ProducesNoDuplicateOutboxMessage()
    {
        var sku = UniqueSku("OUT-DUP");
        var reservationId = await _fixture.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 3,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.OutboxMessages
                .Where(message => message.Type == InventoryReservationExpiredIntegrationEvent.EventType)
                .ExecuteDeleteAsync();
        }

        // First run expires the reservation and stages its outbox record. The second run finds nothing
        // pending, so it emits no second outbox record: exactly one record survives per expiry.
        var first = await _fixture.RunExpirationProcessorAsync(maxResults: 100);
        var second = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.True(first >= 1);
        Assert.Equal(0, second);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var matching = await FindMessagesAsync(verifyContext, reservationId);

        Assert.Single(matching);

        await verifyContext.OutboxMessages
            .Where(outbox => matching.Select(message => message.Id).Contains(outbox.Id))
            .ExecuteDeleteAsync();
    }

    private static async Task<List<OutboxMessage>> FindMessagesAsync(
        InventoryDbContext context,
        Guid reservationId)
    {
        var messages = await context.OutboxMessages
            .Where(message => message.Type == InventoryReservationExpiredIntegrationEvent.EventType)
            .ToListAsync();

        return messages
            .Where(message =>
            {
                var payload = JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(message.Payload);
                return payload is not null && payload.ReservationId == reservationId;
            })
            .ToList();
    }

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}