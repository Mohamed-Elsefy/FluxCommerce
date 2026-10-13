using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.10: end-to-end verification of the complete messaging path against the real SQL Server
/// engine and the real RabbitMQ broker:
///
///   Business Operation -> Business State + Outbox (atomic commit) -> Outbox Dispatcher -> RabbitMQ
///
/// Unlike the Phase 6.7/6.8/6.9 dispatcher tests (which seed Outbox rows directly), these tests drive
/// a real Orders business operation (checkout, cancellation) through <c>ICheckoutStore</c>/<c>IOrderStore</c>
/// so the transaction boundary, the Outbox interceptor, the dispatcher, and the publisher are all
/// exercised together. Covered scenarios:
///
///  A. Successful publication - order + outbox commit atomically, dispatcher publishes, outbox reaches
///     Published, RabbitMQ receives the event and its metadata.
///  B. RabbitMQ failure and recovery - a broker nack keeps the business state, records a retryable
///     failure (message never lost), and a later cycle republishes after the backoff policy.
///  C. Transaction rollback - a rejected duplicate checkout persists neither the order nor its outbox.
///  D. Dispatcher recovery - a stale InProgress claim (post-crash) is recovered and published.
///  E. Batch processing - multiple messages processed; one failed publish does not block the others and
///     nothing is silently lost.
///  F. At-Least-Once boundary - a publish that succeeded but was never marked Published is retried,
///     proving duplicates are possible and exactly-once is never claimed (consumer idempotency is
///     explicitly deferred to Phase 7).
///
/// The collection has parallelization disabled so these tests never overlap the other collections
/// that share the orders database or the test exchange.
/// </summary>
[Collection(SqlServerOrdersDispatcherTestCollection.Name)]
public class OrdersMessagingEndToEndTests
{
    private readonly OrdersOutboxDispatcherTestFixture _fixture;

    public OrdersMessagingEndToEndTests(OrdersOutboxDispatcherTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Checkout_PublishesOrderCreated_AndOutboxReachesPublished()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCreatedIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var (order, created, _, correlationId, recordId, _) = await PlaceOrderAsync(scope);

        try
        {
            using var dispatcherScope = _fixture.Services.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            // Business state persisted.
            Assert.NotNull(await context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == order.Id));

            // Outbox reached Published.
            var message = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == created.EventId);
            Assert.Equal(OutboxStatus.Published, message.Status);

            // RabbitMQ received the event with the originating metadata.
            var delivered = await ReceiveAsync(channel, queueName, 1);
            var delivery = Assert.Single(delivered);
            var deliveredEvent = DeserializeCreated(delivery);
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, delivery.RoutingKey);
            Assert.Equal(OrdersOutboxDispatcherTestFixture.TestExchangeName, delivery.Exchange);
            Assert.Equal(created.EventId, deliveredEvent.EventId);
            Assert.Equal(order.Id.Value, deliveredEvent.OrderId);
            Assert.Equal(correlationId, deliveredEvent.CorrelationId);
            Assert.Equal(created.EventId.ToString(), delivery.BasicProperties.MessageId);
            Assert.Equal(correlationId.ToString(), delivery.BasicProperties.CorrelationId);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(scope, [created.EventId], [order.Id], [recordId]);
        }
    }

    [Fact]
    public async Task Checkout_WhenBrokerNacks_KeepsBusinessState_RecordsRetryableFailure_AndRetriesUntilPublished()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCreatedIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var (order, created, messageId, _, recordId, _) = await PlaceOrderAsync(scope);

        try
        {
            // The broker nacks the first publish attempt; subsequent attempts delegate to the real
            // broker so the retry leg verifies an actual delivery.
            var failOnce = new FailOncePublisher(_fixture.Services.GetRequiredService<IRabbitMQPublisher>());
            using var dispatcherProvider = BuildDispatcherProvider(failOnce);
            using var dispatcherScope = dispatcherProvider.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            // Business state remains persisted even though the broker rejected the publication.
            Assert.NotNull(await context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == order.Id));

            // The outbox recorded a retryable failure: the message is not lost.
            var retryable = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            Assert.Equal(OutboxStatus.Pending, retryable.Status);
            Assert.Equal(1, retryable.RetryCount);
            Assert.NotNull(retryable.Error);
            Assert.NotNull(retryable.RetryAfterUtc);

            // Nothing reached the broker on the failed attempt.
            Assert.Empty(await ReceiveAsync(channel, queueName, 1, attempts: 5));

            // Once the backoff window elapses, a later cycle publishes it.
            await WaitUntilRetryEligibleAsync(_fixture.Services, messageId);
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            var published = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            Assert.Equal(OutboxStatus.Published, published.Status);

            var delivered = await ReceiveAsync(channel, queueName, 1);
            Assert.Equal(order.Id.Value, DeserializeCreated(Assert.Single(delivered)).OrderId);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(scope, [created.EventId], [order.Id], [recordId]);
        }
    }

    [Fact]
    public async Task DuplicateCheckout_RollsBackOrderAndOutbox_Atomically()
    {
        using var scope = _fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICheckoutStore>();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var userId = Guid.NewGuid();
        var idempotencyKey = $"checkout-key-{Guid.NewGuid():N}";

        var firstOrder = CreateOrder(OrderId.Create(), userId);
        var firstCreated = firstOrder.GetDomainEvents().OfType<OrderCreated>().Single();
        var firstRecord = CreateRecord(userId, firstOrder.Id, idempotencyKey);
        var firstOrderId = firstOrder.Id;

        try
        {
            var first = await store.PersistAsync(firstOrder, firstRecord, CancellationToken.None);
            Assert.Equal(CheckoutPersistStatus.Created, first.Status);

            var secondOrder = CreateOrder(OrderId.Create(), userId);
            var secondCreated = secondOrder.GetDomainEvents().OfType<OrderCreated>().Single();
            var secondOrderId = secondOrder.Id;
            var secondRecord = CreateRecord(userId, secondOrder.Id, idempotencyKey);

            var second = await store.PersistAsync(secondOrder, secondRecord, CancellationToken.None);

            Assert.Equal(CheckoutPersistStatus.Duplicate, second.Status);
            Assert.Equal(firstOrderId.Value, second.Existing!.OrderId);

            // The rejected operation's business state is absent ...
            Assert.Null(await context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == secondOrderId));
            // ... and its outbox record is absent too: nothing committed independently.
            Assert.False(await context.OutboxMessages.AnyAsync(m => m.EventId == secondCreated.EventId));

            // The winning order and its outbox remain committed and pending.
            Assert.NotNull(await context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == firstOrderId));
            var firstMessage = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == firstCreated.EventId);
            Assert.Equal(OutboxStatus.Pending, firstMessage.Status);
        }
        finally
        {
            await CleanupAsync(scope, [firstCreated.EventId], [firstOrderId], [firstRecord.Id]);
        }
    }

    [Fact]
    public async Task StaleInProgressClaim_AfterCrash_IsRecoveredAndPublished()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCreatedIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var (order, created, messageId, _, recordId, _) = await PlaceOrderAsync(scope);

        try
        {
            // Simulate a dispatcher crash after claiming: the message is stuck InProgress well beyond
            // the claim timeout (a committed business state whose publication never completed).
            await context.OutboxMessages
                .Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, OutboxStatus.InProgress)
                    .SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow.AddMinutes(-10)));

            using var dispatcherScope = _fixture.Services.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            // The stale claim is recovered, claimed, published, and marked Published.
            var message = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            Assert.Equal(OutboxStatus.Published, message.Status);

            var delivered = await ReceiveAsync(channel, queueName, 1);
            Assert.Equal(order.Id.Value, DeserializeCreated(Assert.Single(delivered)).OrderId);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(scope, [created.EventId], [order.Id], [recordId]);
        }
    }

    [Fact]
    public async Task BatchCheckout_OneFailedPublish_DoesNotBlockOthers_AndNothingIsLost()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCreatedIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var placed = new List<(Order Order, OrderCreated Created, Guid MessageId, Guid RecordId, Guid UserId)>();
        for (var index = 0; index < 3; index++)
        {
            var (order, created, messageId, _, recordId, userId) = await PlaceOrderAsync(scope);
            placed.Add((order, created, messageId, recordId, userId));
        }

        try
        {
            // Exactly one publish in the batch is nacked (whichever message is claimed first); the
            // rest must still publish - one failure must not block the batch.
            var failOnce = new FailOncePublisher(_fixture.Services.GetRequiredService<IRabbitMQPublisher>());
            using var dispatcherProvider = BuildDispatcherProvider(failOnce);
            using var dispatcherScope = dispatcherProvider.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            var eventIds = placed.Select(p => p.Created.EventId).ToList();
            var afterFirstDispatch = await context.OutboxMessages
                .AsNoTracking()
                .Where(m => eventIds.Contains(m.EventId))
                .ToListAsync();

            Assert.Equal(2, afterFirstDispatch.Count(m => m.Status == OutboxStatus.Published));
            var retryable = afterFirstDispatch.Single(m => m.Status == OutboxStatus.Pending);
            Assert.Equal(1, retryable.RetryCount);
            Assert.NotNull(retryable.RetryAfterUtc);

            // The two successful publications already reached the broker.
            var partial = await ReceiveAsync(channel, queueName, 2, attempts: 20);
            Assert.Equal(2, partial.Select(DeserializeCreated).Select(e => e.OrderId).Distinct().Count());

            // Once the failed message's backoff window elapses it is retried - nothing is lost.
            await WaitUntilRetryEligibleAsync(_fixture.Services, retryable.Id);
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            var remainder = await ReceiveAsync(channel, queueName, 1, attempts: 20);
            var allOrderIds = partial
                .Concat(remainder)
                .Select(DeserializeCreated)
                .Select(e => e.OrderId)
                .ToList();

            Assert.Equal(3, allOrderIds.Distinct().Count());
            Assert.All(placed, p => Assert.Contains(p.Order.Id.Value, allOrderIds));

            var afterRetry = await context.OutboxMessages
                .AsNoTracking()
                .Where(m => eventIds.Contains(m.EventId))
                .ToListAsync();
            Assert.All(afterRetry, m => Assert.Equal(OutboxStatus.Published, m.Status));
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(scope, placed.Select(p => p.Created.EventId), placed.Select(p => p.Order.Id), placed.Select(p => p.RecordId));
        }
    }

    [Fact]
    public async Task ConfirmedPublish_CrashBeforeMarkPublished_IsRepublished_AtLeastOnce()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCreatedIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        // The observable state after a crash between a confirmed publish and MarkPublished: the
        // message already reached the broker, but its outbox row is still Pending.
        var correlationId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var eventId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-ALO",
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero))
        {
            EventId = eventId,
            CorrelationId = correlationId
        };
        await context.OutboxMessages.AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Type = OrderCreatedIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(@event),
            CorrelationId = @event.CorrelationId,
            CausationId = @event.CausationId,
            OccurredAtUtc = @event.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null
        });
        await context.SaveChangesAsync();

        // The "first" publish - the one that succeeded just before the crash - reached the broker.
        var publisher = _fixture.Services.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, @event);
        Assert.True(confirmed);

        try
        {
            using var dispatcherScope = _fixture.Services.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            var message = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == @event.EventId);
            Assert.Equal(OutboxStatus.Published, message.Status);

            // The broker holds TWO deliveries of the same EventId: the first publish and the retry.
            // At-Least-Once delivery makes duplicates possible; exactly-once is never claimed, and
            // duplicate-safe consumer processing is explicitly deferred to Phase 7.
            var delivered = await ReceiveAsync(channel, queueName, 2);
            Assert.Equal(2, delivered.Count);
            Assert.All(delivered, d =>
            {
                var deliveredEvent = DeserializeCreated(d);
                Assert.Equal(@event.EventId, deliveredEvent.EventId);
                Assert.Equal(correlationId, deliveredEvent.CorrelationId);
            });
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await context.OutboxMessages
                .Where(m => m.EventId == @event.EventId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CancelOrder_PublishesOrderCancelled_AndOutboxReachesPublished()
    {
        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel, OrderCancelledIntegrationEvent.EventType);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages.ExecuteDeleteAsync();

        var (order, created, _, _, recordId, userId) = await PlaceOrderAsync(scope);

        try
        {
            // The cancellation business operation: transition the tracked aggregate and flush. The
            // interceptor adds the OrderCancelled outbox record to the same save as the state change.
            var orderStore = scope.ServiceProvider.GetRequiredService<IOrderStore>();
            var loaded = await orderStore.GetOwnedOrderAsync(new CustomerId(userId), order.Id, CancellationToken.None);
            Assert.NotNull(loaded);
            var cancellation = loaded!.Cancel(new DateTimeOffset(DateTime.UtcNow));
            Assert.True(cancellation.IsSuccess);

            // The OrderCancelled event must be captured before the save: the pending domain events on
            // the tracked aggregate are consumed as the outbox interceptor processes the save.
            var cancelled = loaded.GetDomainEvents().OfType<OrderCancelled>().Single();

            await orderStore.SaveChangesAsync(CancellationToken.None);

            using var dispatcherScope = _fixture.Services.CreateScope();
            var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            // Both the created and the cancelled outbox records reach Published.
            var cancelledMessage = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == cancelled.EventId);
            Assert.Equal(OutboxStatus.Published, cancelledMessage.Status);
            Assert.Equal(OrderCancelledIntegrationEvent.EventType, cancelledMessage.Type);

            // RabbitMQ receives the OrderCancelled event with the originating metadata.
            var delivered = await ReceiveAsync(channel, queueName, 1);
            var delivery = Assert.Single(delivered);
            var deliveredEvent = JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(delivery.Body);
            Assert.NotNull(deliveredEvent);
            Assert.Equal(OrderCancelledIntegrationEvent.EventType, delivery.RoutingKey);
            Assert.Equal(order.Id.Value, deliveredEvent.OrderId);
            Assert.Equal(cancelled.EventId, deliveredEvent.EventId);
            Assert.Equal(cancelled.EventId.ToString(), delivery.BasicProperties.MessageId);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(scope, [created.EventId], [order.Id], [recordId]);
        }
    }

    private async Task<(Order Order, OrderCreated Created, Guid MessageId, Guid CorrelationId, Guid RecordId, Guid UserId)> PlaceOrderAsync(
        IServiceScope scope)
    {
        var store = scope.ServiceProvider.GetRequiredService<ICheckoutStore>();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var userId = Guid.NewGuid();
        var orderId = OrderId.Create();
        var order = CreateOrder(orderId, userId);
        var created = order.GetDomainEvents().OfType<OrderCreated>().Single();
        var record = CreateRecord(userId, order.Id);

        var result = await store.PersistAsync(order, record, CancellationToken.None);
        Assert.Equal(CheckoutPersistStatus.Created, result.Status);

        var message = await context.OutboxMessages.SingleAsync(m => m.EventId == created.EventId);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.NotEqual(Guid.Empty, message.CorrelationId);

        return (order, created, message.Id, message.CorrelationId, record.Id, userId);
    }

    private static Order CreatedOrder(string name) => throw new NotSupportedException();

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

    private static async Task WaitUntilRetryEligibleAsync(IServiceProvider services, Guid id)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var message = await context.OutboxMessages
                .AsNoTracking()
                .SingleAsync(m => m.Id == id);

            if (message.RetryAfterUtc is null || message.RetryAfterUtc.Value <= DateTime.UtcNow.AddSeconds(-0.5))
                return;

            await Task.Delay(100);
        }

        Assert.Fail("The outbox message did not become retry-eligible within the timeout.");
    }

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

    private static ServiceProvider BuildDispatcherProvider(
        IRabbitMQPublisher publisher,
        Action<OutboxDispatchOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddOrdersInfrastructure(OrdersOutboxDispatcherTestFixture.ConnectionString);

        services.AddLogging();
        services.AddSingleton<IRabbitMQPublisher>(publisher);

        services.Configure<OutboxDispatchOptions>(options =>
        {
            options.ClaimTimeout = TimeSpan.FromSeconds(5);
            configure?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    private static async Task<string> DeclareQueueAsync(IChannel channel, string eventType)
    {
        var queueName = $"it.e2e.{Guid.NewGuid():N}";

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null!,
            passive: false,
            noWait: false,
            cancellationToken: default);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: OrdersOutboxDispatcherTestFixture.TestExchangeName,
            routingKey: eventType,
            arguments: null!,
            noWait: false,
            cancellationToken: default);

        return queueName;
    }

    private static async Task<List<(string RoutingKey, string Exchange, byte[] Body, IReadOnlyBasicProperties BasicProperties)>> ReceiveAsync(
        IChannel channel,
        string queueName,
        int expectedCount,
        int attempts = 100)
    {
        var delivered = new List<(string RoutingKey, string Exchange, byte[] Body, IReadOnlyBasicProperties BasicProperties)>();

        for (var attempt = 0; attempt < attempts && delivered.Count < expectedCount; attempt++)
        {
            var result = await channel.BasicGetAsync(queue: queueName, autoAck: true, cancellationToken: default);

            if (result is not null)
            {
                delivered.Add((
                    result.RoutingKey,
                    result.Exchange,
                    result.Body.ToArray(),
                    result.BasicProperties));
            }
            else
            {
                await Task.Delay(100);
            }
        }

        return delivered;
    }

    private static OrderCreatedIntegrationEvent DeserializeCreated(
        (string RoutingKey, string Exchange, byte[] Body, IReadOnlyBasicProperties BasicProperties) delivery) =>
        JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(delivery.Body)!;

    private sealed class FailOncePublisher : IRabbitMQPublisher
    {
        private readonly IRabbitMQPublisher _inner;
        private bool _alreadyFailed;

        public FailOncePublisher(IRabbitMQPublisher inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<bool> PublishAsync(
            string eventType,
            IIntegrationEvent @event,
            CancellationToken cancellationToken = default)
        {
            if (!_alreadyFailed)
            {
                _alreadyFailed = true;
                return Task.FromResult(false);
            }

            return _inner.PublishAsync(eventType, @event, cancellationToken);
        }
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public bool IsAuthenticated => false;
    }
}

