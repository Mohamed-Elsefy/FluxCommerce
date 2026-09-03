using System.Text.Json;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.3: the Orders Outbox records are created transactionally with the business state on the
/// real SQL Server engine. A successful checkout commits both the Order and its OrderCreated outbox
/// record in one transaction; a duplicate checkout rolls the whole save back, so no outbox record
/// survives for the rejected order.
/// </summary>
[Collection(SqlServerOrdersTestCollection.Name)]
public class OrdersOutboxIntegrationTests
{
    private readonly SqlServerOrdersTestFixture _fixture;

    public OrdersOutboxIntegrationTests(SqlServerOrdersTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Checkout_PersistsOrderAndOrderCreatedOutbox_Atomically()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICheckoutStore>();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var userId = Guid.NewGuid();
        var orderId = OrderId.Create();
        var order = CreateOrder(orderId, userId);
        var createdEvent = order.GetDomainEvents().OfType<OrderCreated>().Single();
        var record = CreateRecord(userId, orderId);

        try
        {
            var result = await store.PersistAsync(order, record, CancellationToken.None);

            Assert.Equal(CheckoutPersistStatus.Created, result.Status);

            var message = await context.OutboxMessages.SingleAsync(m => m.EventId == createdEvent.EventId);

            Assert.Equal(OrderCreatedIntegrationEvent.EventType, message.Type);
            Assert.Equal(OutboxStatus.Pending, message.Status);
            Assert.Equal(0, message.RetryCount);
            Assert.Null(message.CausationId);
            Assert.Equal(createdEvent.OccurredAtUtc, message.OccurredAtUtc);
            Assert.NotEqual(Guid.Empty, message.CorrelationId);

            var payload = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Payload);

            Assert.NotNull(payload);
            Assert.Equal(orderId.Value, payload!.OrderId);
            Assert.Equal(userId, payload.CustomerId);
        }
        finally
        {
            await CleanupAsync(scope, [createdEvent.EventId], [orderId], [record.Id]);
        }
    }

    [Fact]
    public async Task DuplicateCheckout_RollsBackOrderAndOutbox()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICheckoutStore>();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var userId = Guid.NewGuid();
        var idempotencyKey = $"checkout-key-{Guid.NewGuid():N}";

        var firstOrderId = OrderId.Create();
        var firstOrder = CreateOrder(firstOrderId, userId);
        var firstEvent = firstOrder.GetDomainEvents().OfType<OrderCreated>().Single();
        var firstRecord = CreateRecord(userId, firstOrderId, idempotencyKey);

        try
        {
            var first = await store.PersistAsync(firstOrder, firstRecord, CancellationToken.None);
            Assert.Equal(CheckoutPersistStatus.Created, first.Status);

            var secondOrderId = OrderId.Create();
            var secondOrder = CreateOrder(secondOrderId, userId);
            var secondEvent = secondOrder.GetDomainEvents().OfType<OrderCreated>().Single();
            var secondRecord = CreateRecord(userId, secondOrderId, idempotencyKey);

            var second = await store.PersistAsync(secondOrder, secondRecord, CancellationToken.None);

            Assert.Equal(CheckoutPersistStatus.Duplicate, second.Status);
            Assert.Equal(firstOrderId.Value, second.Existing!.OrderId);

            Assert.False(await context.OutboxMessages.AnyAsync(m => m.EventId == secondEvent.EventId));
            Assert.Null(await context.Orders.FirstOrDefaultAsync(o => o.Id == secondOrderId));
        }
        finally
        {
            await CleanupAsync(scope, [firstEvent.EventId], [firstOrderId], [firstRecord.Id]);
        }
    }

    private static Order CreateOrder(OrderId orderId, Guid userId) =>
        Order.Place(
            orderId,
            new CustomerId(userId),
            OrderNumber.Create(),
            CreateAddress(),
            CreateAddress(),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku("SKU-1"),
                    "Test Product",
                    new OrderMoney(10m, "USD"),
                    1),
            ],
            DateTimeOffset.UtcNow);

    private static OrderAddress CreateAddress() =>
        new("Shipping Recipient", "Shipping Line 1", null, "Shipping City", "Region", "1000", "US");

    private static CheckoutIdempotencyRecord CreateRecord(
        Guid userId,
        OrderId orderId,
        string? idempotencyKey = null) =>
        new(
            Guid.NewGuid(),
            userId,
            idempotencyKey ?? $"checkout-key-{Guid.NewGuid():N}",
            orderId.Value,
            "fingerprint",
            DateTimeOffset.UtcNow);

    private static async Task CleanupAsync(
        IServiceScope scope,
        IEnumerable<Guid> eventIds,
        IEnumerable<OrderId> orderIds,
        IEnumerable<Guid> recordIds)
    {
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var eventIdList = eventIds.ToList();
        if (eventIdList.Count > 0)
        {
            await context.OutboxMessages
                .Where(m => eventIdList.Contains(m.EventId))
                .ExecuteDeleteAsync();
        }

        var orderIdList = orderIds.ToList();
        if (orderIdList.Count > 0)
        {
            await context.Orders
                .Where(o => orderIdList.Contains(o.Id))
                .ExecuteDeleteAsync();
        }

        var recordIdList = recordIds.ToList();
        if (recordIdList.Count > 0)
        {
            await context.CheckoutIdempotencies
                .Where(record => recordIdList.Contains(record.Id))
                .ExecuteDeleteAsync();
        }
    }
}
