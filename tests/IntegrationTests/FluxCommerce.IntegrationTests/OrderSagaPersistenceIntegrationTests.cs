using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.2 Order/Inventory Saga persistence on the real SQL Server engine. The saga rows survive the
/// module migrations, are reloaded by Order and by correlation id, and the two database guarantees the
/// workflow relies on hold: the unique index (one saga per order, so the same OrderCreated event cannot
/// start a second workflow) and the optimistic concurrency token (so two conflicting Inventory results
/// cannot both be applied).
/// </summary>
[Collection(SqlServerOrdersTestCollection.Name)]
public class OrderSagaPersistenceIntegrationTests
{
    private readonly SqlServerOrdersTestFixture _fixture;

    public OrderSagaPersistenceIntegrationTests(SqlServerOrdersTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAndSave_PersistsSaga_AndReloadsByOrderId()
    {
        var saga = CreateSaga();

        try
        {
            using (var scope = _fixture.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
                store.Add(saga);
                await store.SaveChangesAsync();
            }

            using var reloadScope = _fixture.Services.CreateScope();
            var reloadStore = reloadScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
            var loaded = await reloadStore.GetByOrderIdAsync(saga.OrderId);

            Assert.NotNull(loaded);
            Assert.Equal(saga.Id, loaded.Id);
            Assert.Equal(saga.OrderId, loaded.OrderId);
            Assert.Equal(saga.CorrelationId, loaded.CorrelationId);
            Assert.Equal(OrderSagaState.Started, loaded.State);
            Assert.Equal(saga.CreatedAtUtc, loaded.CreatedAtUtc);
            Assert.Equal(saga.UpdatedAtUtc, loaded.UpdatedAtUtc);
            Assert.Equal(OrderSaga.InitialVersion, loaded.Version);
        }
        finally
        {
            await CleanupAsync([saga.OrderId]);
        }
    }

    [Fact]
    public async Task AddAndSave_ReloadsSagaByCorrelationId()
    {
        var saga = CreateSaga();

        try
        {
            using (var scope = _fixture.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
                store.Add(saga);
                await store.SaveChangesAsync();
            }

            using var reloadScope = _fixture.Services.CreateScope();
            var reloadStore = reloadScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
            var loaded = await reloadStore.GetByCorrelationIdAsync(saga.CorrelationId);

            Assert.NotNull(loaded);
            Assert.Equal(saga.OrderId, loaded.OrderId);
        }
        finally
        {
            await CleanupAsync([saga.OrderId]);
        }
    }

    [Fact]
    public async Task Transition_AppliesAndPersistsStateAndVersion()
    {
        var saga = CreateSaga();
        var transitionedAt = saga.CreatedAtUtc.AddMinutes(1);

        try
        {
            using (var scope = _fixture.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
                store.Add(saga);
                await store.SaveChangesAsync();
            }

            using (var transitionScope = _fixture.Services.CreateScope())
            {
                var store = transitionScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
                var loaded = await store.GetByOrderIdAsync(saga.OrderId);

                Assert.NotNull(loaded);
                var result = loaded.RequestReservation(transitionedAt);

                Assert.True(result.IsSuccess);
                await store.SaveChangesAsync();
            }

            using var reloadScope = _fixture.Services.CreateScope();
            var reloadStore = reloadScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
            var final = await reloadStore.GetByOrderIdAsync(saga.OrderId);

            Assert.NotNull(final);
            Assert.Equal(OrderSagaState.WaitingForInventory, final.State);
            Assert.Equal(OrderSaga.InitialVersion + 1, final.Version);
            Assert.Equal(transitionedAt, final.UpdatedAtUtc);
        }
        finally
        {
            await CleanupAsync([saga.OrderId]);
        }
    }

    [Fact]
    public async Task AddTwoSagas_WithSameOrderId_IsRejectedByUniqueIndex()
    {
        var orderId = OrderId.Create();

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            context.OrderSagas.Add(CreateSaga(orderId: orderId));
            context.OrderSagas.Add(CreateSaga(orderId: orderId));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync([orderId]);
        }
    }

    [Fact]
    public async Task SaveChanges_WithStaleVersion_ThrowsConcurrencyConflict()
    {
        var saga = CreateSaga();

        try
        {
            using (var scope = _fixture.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
                store.Add(saga);
                await store.SaveChangesAsync();
            }

            // Two consumers observe the saga after the reservation request; both see Version = 2.
            using var firstScope = _fixture.Services.CreateScope();
            var firstStore = firstScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
            var first = await firstStore.GetByOrderIdAsync(saga.OrderId);
            Assert.NotNull(first);
            Assert.True(first.RequestReservation(saga.CreatedAtUtc.AddMinutes(1)).IsSuccess);
            await firstStore.SaveChangesAsync();

            using var secondScope = _fixture.Services.CreateScope();
            var secondStore = secondScope.ServiceProvider.GetRequiredService<IOrderSagaStore>();
            var second = await secondStore.GetByOrderIdAsync(saga.OrderId);
            Assert.NotNull(second);

            // The first consumer applies the success result and commits (Version 2 -> 3).
            Assert.True(first.Complete(saga.CreatedAtUtc.AddMinutes(2)).IsSuccess);
            await firstStore.SaveChangesAsync();

            // The second consumer applies the failure result on its stale copy; the version guard
            // rejects the conflicting concurrent result instead of silently overwriting.
            Assert.True(second.Fail(saga.CreatedAtUtc.AddMinutes(3)).IsSuccess);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondStore.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync([saga.OrderId]);
        }
    }

    [Fact]
    public async Task Migrations_CreateTheOrderSagasTable()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var tableCount = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'OrderSagas'")
            .SingleAsync();

        Assert.Equal(1, tableCount);
    }

    private static OrderSaga CreateSaga(OrderId? orderId = null) =>
        OrderSaga.Start(
            Guid.NewGuid(),
            orderId ?? OrderId.Create(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 19, 8, 15, 0, TimeSpan.Zero));

    private async Task CleanupAsync(IReadOnlyCollection<OrderId> orderIds)
    {
        if (orderIds.Count == 0)
            return;

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OrderSagas
            .Where(saga => orderIds.Contains(saga.OrderId))
            .ExecuteDeleteAsync();
    }
}