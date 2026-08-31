using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Outbox;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 6.3: the Inventory outbox interceptor appends Outbox records inside the same SaveChangesAsync
/// that writes the business state. Expiring a reservation produces exactly one selected outbox record;
/// a lifecycle save without the selected event (e.g. a commit) produces none.
/// </summary>
public class InventoryOutboxSaveChangesInterceptorTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 10, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryOutboxSaveChangesInterceptorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task ExpiringReservation_WritesReservationExpiredOutbox_Atomically()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-1"), onHandQuantity: 10, CreatedAtUtc);
        item.Reserve(3, CreatedAtUtc);

        var reservation = InventoryReservation.CreateReserved(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            expiresAt: CreatedAtUtc.AddMinutes(5),
            utcNow: CreatedAtUtc).Value;

        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);

        reservation.Expire(item, CreatedAtUtc.AddMinutes(10));
        var expiredEvent = reservation.GetDomainEvents().OfType<InventoryReservationExpired>().Single();

        await context.SaveChangesAsync();

        var message = await context.OutboxMessages.SingleAsync(m => m.EventId == expiredEvent.EventId);

        Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, message.Type);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.CausationId);
        Assert.Equal(expiredEvent.OccurredAtUtc, message.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, message.CorrelationId);
    }

    [Fact]
    public async Task ExpiringReservation_BusinessStateAndOutbox_RollBackAtomically_WithTransaction()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-RB"), onHandQuantity: 10, CreatedAtUtc);
        item.Reserve(3, CreatedAtUtc);

        var reservation = InventoryReservation.CreateReserved(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            expiresAt: CreatedAtUtc.AddMinutes(5),
            utcNow: CreatedAtUtc).Value;

        reservation.Expire(item, CreatedAtUtc.AddMinutes(10));

        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);

        // The reservation expiry state change and the outbox insert are written inside the same
        // ambient transaction (the interceptor appends the outbox record on the very SaveChangesAsync
        // that persists the business state). Rolling the transaction back discards BOTH together.
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.OutboxMessages.CountAsync());

        await transaction.RollbackAsync();

        // A fresh context on the same connection sees neither the expired reservation nor the
        // outbox record: the unit is atomic, so a failed business commit can never strand an
        // outbox message.
        await using var verify = CreateContext();
        Assert.False(await verify.OutboxMessages.AnyAsync());
        Assert.False(await verify.InventoryReservations.AnyAsync());
    }

    [Fact]
    public async Task ExpiringReservation_BusinessStateAndOutbox_PersistTogether_AndAreVisibleInSameTransaction()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-ATOMIC"), onHandQuantity: 10, CreatedAtUtc);
        item.Reserve(3, CreatedAtUtc);

        var reservation = InventoryReservation.CreateReserved(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            expiresAt: CreatedAtUtc.AddMinutes(5),
            utcNow: CreatedAtUtc).Value;

        reservation.Expire(item, CreatedAtUtc.AddMinutes(10));

        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);

        await context.SaveChangesAsync();

        // Both the reservation (now expired) and exactly one outbox record exist in the database,
        // committed together by a single save.
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task RepeatedExpirationOfSameReservation_ProducesNoDuplicateOutboxMessage()
    {
        // Unit of work 1: the reservation expires, producing exactly one domain event and one outbox
        // record. After a successful save the production DomainEventInterceptor clears the aggregate's
        // pending domain events.
        await using (var context = CreateContext())
        {
            var item = InventoryItem.Create(new InventorySku("SKU-DUP"), onHandQuantity: 10, CreatedAtUtc);
            item.Reserve(3, CreatedAtUtc);

            var reservation = InventoryReservation.CreateReserved(
                InventoryReservationId.Create(),
                OrderId.Create(),
                item.Sku,
                3,
                expiresAt: CreatedAtUtc.AddMinutes(5),
                utcNow: CreatedAtUtc).Value;

            context.InventoryItems.Add(item);
            context.InventoryReservations.Add(reservation);

            reservation.Expire(item, CreatedAtUtc.AddMinutes(10));
            await context.SaveChangesAsync();

            Assert.Equal(1, await context.OutboxMessages.CountAsync());
        }

        // Unit of work 2: the now-expired reservation is offered again (e.g. a duplicate expiration
        // pass). The Domain rejects the re-expire before any state change or domain event, so this
        // save emits no second outbox record.
        await using (var context = CreateContext())
        {
            var item = await context.InventoryItems.SingleAsync();
            var reservation = await context.InventoryReservations.SingleAsync();

            reservation.Expire(item, CreatedAtUtc.AddMinutes(20));
            await context.SaveChangesAsync();

            Assert.Equal(1, await context.OutboxMessages.CountAsync());
        }
    }

    [Fact]
    public async Task CommittingReservation_WritesNoOutbox()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-2"), onHandQuantity: 10, CreatedAtUtc);
        item.Reserve(3, CreatedAtUtc);

        var reservation = InventoryReservation.CreateReserved(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            expiresAt: CreatedAtUtc.AddMinutes(5),
            utcNow: CreatedAtUtc).Value;

        reservation.Commit(item, CreatedAtUtc);

        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);

        await context.SaveChangesAsync();

        Assert.False(await context.OutboxMessages.AnyAsync());
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private InventoryDbContext CreateContext()
    {
        var outboxInterceptor = new InventoryOutboxSaveChangesInterceptor(
            new InventoryOutboxMessageFactory(new FixedCorrelationIdProvider(Guid.NewGuid())));

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(outboxInterceptor)
            .Options;

        var context = new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private sealed class FixedCorrelationIdProvider : ICorrelationIdProvider
    {
        private readonly Guid _value;

        public FixedCorrelationIdProvider(Guid value) => _value = value;

        public Guid GetCorrelationId() => _value;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}