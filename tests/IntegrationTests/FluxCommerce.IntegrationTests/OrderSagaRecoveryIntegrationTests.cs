using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using ContractReservationStatus = FluxCommerce.Modules.Inventory.Contracts.Enums.InventoryReservationStatus;
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.8 verification (plan §13): the Saga remains correct under the realistic distributed-system
/// failure modes — duplicate result, concurrent result, conflicting result, consumer crash before ACK
/// (redelivery), and process restart (saga recovery) — against the real SQL Server engines and the real
/// RabbitMQ broker, together with the Phase 8.8 hardening of the late-reservation-after-release race:
/// a reservation result that arrives after the compensation release was requested is compensated
/// immediately instead of leaking until Inventory expiry. Every scenario asserts that no message is
/// dead-lettered and no business effect is duplicated.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class OrderSagaRecoveryIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public OrderSagaRecoveryIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentReservedResults_ProduceSingleTransition_AndNothingIsDeadLettered()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        var correlationId = await SeedSagaAsync(orderId);
        var first = CreateReservedEvent(orderId);
        var second = CreateReservedEvent(orderId);

        // Two consumers on the same queue: the broker round-robins the two distinct reserved results to
        // them concurrently (prefetch 1 each). The saga version token serializes the transition — at
        // most one applies the Completed transition; the other re-applies idempotently on the completed
        // saga. Both results are acknowledged and nothing is dead-lettered.
        await using var provider = _fixture.BuildProvider(queueName, options => options.Concurrency = 2);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, first);
            await PublishAsync(provider, second);

            await WaitForOrdersProcessedAsync(first.EventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForOrdersProcessedAsync(second.EventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(first.EventId, InventoryReservedConsumer.StableHandlerName));
            Assert.Equal(1, await CountOrdersProcessedAsync(second.EventId, InventoryReservedConsumer.StableHandlerName));

            // Single valid transition: the saga is Completed exactly once, the order stays Pending and
            // both reservations are recorded (each result carried its own reservation id).
            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Completed, saga!.State);
            Assert.Equal(OrderSaga.InitialVersion + 2, saga.Version);

            Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);
            Assert.Equal(2, saga.ReservationIds.Count);
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [correlationId], [first.EventId, second.EventId]);
        }
    }

    [Fact]
    public async Task ConflictingReservedThenFailed_IsRejectedSafely()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        var correlationId = await SeedSagaAsync(orderId);
        var reserved = CreateReservedEvent(orderId);
        var failed = CreateFailedEvent(orderId);
        await using var provider = _fixture.BuildProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, reserved);
            await WaitForOrdersProcessedAsync(reserved.EventId, InventoryReservedConsumer.StableHandlerName);

            // The conflicting failure result is a VALID delivery (it is processed and acknowledged), but
            // its transition is rejected: the saga is already Completed, so it must not flip it or cancel
            // the order.
            await PublishAsync(provider, failed);
            await WaitForOrdersProcessedAsync(failed.EventId, InventoryReservationFailedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(failed.EventId, InventoryReservationFailedConsumer.StableHandlerName));

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Completed, saga!.State);
            Assert.Single(saga.ReservationIds);

            Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);

            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                Assert.Equal(0, await context.OutboxMessages.CountAsync(
                    message => message.Type == OrderCancelledIntegrationEvent.EventType
                        && message.Payload.Contains(orderId.ToString())));
            }

            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [correlationId], [reserved.EventId, failed.EventId]);
        }
    }

    [Fact]
    public async Task ConflictingFailedThenReserved_IsRejectedSafely()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        var correlationId = await SeedSagaAsync(orderId);
        var reserved = CreateReservedEvent(orderId);
        var failed = CreateFailedEvent(orderId);
        await using var provider = _fixture.BuildProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, failed);
            await WaitForOrdersProcessedAsync(failed.EventId, InventoryReservationFailedConsumer.StableHandlerName);

            // The conflicting success result is processed but rejected: the saga is already Failed, so it
            // must not record the reservation or resurrect the order.
            await PublishAsync(provider, reserved);
            await WaitForOrdersProcessedAsync(reserved.EventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(reserved.EventId, InventoryReservedConsumer.StableHandlerName));

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Failed, saga!.State);
            Assert.Empty(saga.ReservationIds);

            Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderId))!.Status);

            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                Assert.Equal(1, await context.OutboxMessages.CountAsync(
                    message => message.Type == OrderCancelledIntegrationEvent.EventType
                        && message.Payload.Contains(orderId.ToString())));
            }

            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [correlationId], [reserved.EventId, failed.EventId]);
        }
    }

    [Fact]
    public async Task ConsumerCrashBeforeAck_Redelivery_HasNoDuplicateEffect()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        var correlationId = await SeedSagaAsync(orderId);
        var @event = CreateReservedEvent(orderId);

        // Provider A simulates the crashing consumer: it connects so the durable queue is declared, then
        // stops. The queue persists across the provider disposal.
        var providerA = _fixture.BuildProvider(queueName);
        await using var providerADisposable = providerA;
        var consumerA = await StartConsumerAsync(providerA);
        await WaitForConsumerAsync(providerA, queueName);
        await consumerA.StopAsync(CancellationToken.None);

        await PublishAsync(providerA, @event);

        // "Consumer A" fetches the delivery without auto-ack and processes it through the real pipeline:
        // the business effect commits (saga Completed + ProcessedMessage), but the ACK is never sent.
        var connectionProviderA = providerA.GetRequiredService<IRabbitMQConnectionProvider>();
        await using (var fetchChannel = await connectionProviderA.CreateChannelAsync())
        {
            var fetched = await fetchChannel.BasicGetAsync(queueName, autoAck: false, CancellationToken.None);
            Assert.NotNull(fetched);

            using (var scope = providerA.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<IIntegrationEventMessageProcessor>();
                var action = await processor.ProcessAsync(fetched!.BasicProperties, fetched.Body, CancellationToken.None);
                Assert.Equal(ConsumerMessageAction.Ack, action);
            }

            Assert.Equal(OrderSagaState.Completed, (await GetSagaAsync(orderId))!.State);
            Assert.Equal(1, await CountOrdersProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));

            // "Crash before ACK": the channel closes without acknowledging -> the broker requeues the
            // delivery for redelivery.
        }

        // Provider B is the restarted process: it receives the redelivered message and the idempotent
        // processing boundary recognizes it as already processed, acknowledging it WITHOUT re-executing
        // the business operation.
        await using var providerB = _fixture.BuildProvider(queueName);
        var consumerB = await StartConsumerAsync(providerB);
        await WaitForConsumerAsync(providerB, queueName);
        await WaitForQueueEmptyAsync(providerB, queueName);
        await consumerB.StopAsync(CancellationToken.None);

        Assert.Equal(1, await CountOrdersProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));
        Assert.Equal(OrderSagaState.Completed, (await GetSagaAsync(orderId))!.State);
        Assert.Single((await GetSagaAsync(orderId))!.ReservationIds);
        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);
        Assert.Equal(0u, await GetMessageCountAsync(providerB, DeadLetterQueueName(queueName)));

        await CleanupAsync(providerA, queueName, orderId, [correlationId], [@event.EventId]);
    }

    [Fact]
    public async Task SagaRestart_ResumesForwardFlow_FromPersistedState()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // "Process A" persists the saga (WaitingForInventory) and goes away.
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        await SeedSagaAsync(orderId, correlationId);

        var @event = CreateReservedEvent(orderId);
        await using var provider = _fixture.BuildProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // "Process B" (a fresh DI container) resumes the workflow: it finds the persisted saga and
            // completes it from the surviving state.
            await PublishAsync(provider, @event);

            await WaitForOrdersProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Completed, saga!.State);
            Assert.Equal(correlationId, saga.CorrelationId);
            Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);
            Assert.Equal(1, await CountOrdersProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [correlationId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task SagaRestart_ResumesCompensation_FromPersistedReservations()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var startCorrelationId = Guid.NewGuid();
        var cancellationCorrelationId = Guid.NewGuid();

        // "Process A" persists a COMPLETED saga with a recorded reservation, then goes away.
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid(), "SKU-8.8", 3);
        await SeedSagaAsync(orderId, startCorrelationId);
        await ApplyReservationSuccessAsync(orderId, reservationId, startCorrelationId);

        await using var provider = _fixture.BuildCompensationProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // "Process B" resumes the compensation leg: the cancelled order is compensated from the
            // reservation ids that survived the restart.
            var orderCancelled = new OrderCancelledIntegrationEvent(
                orderId,
                Guid.NewGuid(),
                "ORD-RECOVERY",
                DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = cancellationCorrelationId
            };
            var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
            Assert.True(await publisher.PublishAsync(OrderCancelledIntegrationEvent.EventType, orderCancelled));

            await WaitForOrdersProcessedAsync(orderCancelled.EventId, OrderCancelledSagaConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            var release = await GetOrdersOutboxByCorrelationAsync(cancellationCorrelationId);
            var command = Assert.Single(release);
            Assert.Equal(InventoryReleaseRequestedIntegrationEvent.EventType, command.Type);
            Assert.Equal(cancellationCorrelationId, command.CorrelationId);
            Assert.Equal(orderCancelled.EventId, command.CausationId);
            Assert.Contains(reservationId.ToString(), command.Payload);

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.NotNull(saga!.ReleaseRequestedAtUtc);
            Assert.Equal(reservationId, Assert.Single(saga.ReservationIds));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [startCorrelationId, cancellationCorrelationId], [cancellationCorrelationId]);
        }
    }

    [Fact]
    public async Task LateReservationAfterRelease_IsCompensatedImmediately_EndToEnd()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var skuA = UniqueSku("8.8-LATE-A").ToUpperInvariant();
        var skuB = UniqueSku("8.8-LATE-B").ToUpperInvariant();
        var occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var cancellationCorrelationId = Guid.NewGuid();

        // Two inventory reservations for the cancelled order exist at Inventory (the second was created
        // by a result that was still in flight when the order was cancelled).
        var firstReservationId = await _fixture.SeedReservationAsync(skuA, orderId, quantity: 3, expiresAt: DateTimeOffset.UtcNow.AddMinutes(30), onHand: 10);
        var lateReservationId = await _fixture.SeedReservationAsync(skuB, orderId, quantity: 2, expiresAt: DateTimeOffset.UtcNow.AddMinutes(30), onHand: 5);

        await _fixture.SeedOrderAsync(orderId, customerId, skuA, 3);
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var firstReservedEventId = Guid.Empty;
        var orderCancelledEventId = Guid.Empty;
        var lateReservedEventId = Guid.Empty;
        var lateReservedCorrelationId = Guid.NewGuid();
        var firstReleaseEventId = Guid.Empty;
        var lateReleaseEventId = Guid.Empty;
        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            var startCorrelationId = Guid.NewGuid();

            // 1. The saga is started (WaitingForInventory).
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var result = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId),
                    startCorrelationId,
                    causationId: null,
                    occurredAtUtc);

                Assert.True(result.SagaCreated);
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                await context.SaveChangesAsync();

                // The reservations for this order already exist at Inventory (seeded), so the
                // reservation-request commands staged by the saga start must never be dispatched later
                // — removing them keeps only the compensation release commands flowing through the
                // outbox dispatcher.
                var stagedRequests = await context.OutboxMessages
                    .Where(message =>
                        message.CorrelationId == startCorrelationId
                        && message.Type == InventoryReservationRequestedIntegrationEvent.EventType)
                    .ToListAsync();
                context.OutboxMessages.RemoveRange(stagedRequests);
                await context.SaveChangesAsync();
            }

            // 2. The first reservation result completes the saga and records the reservation.
            var firstReserved = CreateReservedEvent(orderId, firstReservationId, skuA, 3);
            firstReservedEventId = firstReserved.EventId;
            await PublishAsync(provider, firstReserved);
            await WaitForOrdersProcessedAsync(firstReservedEventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            var completedSaga = await GetSagaAsync(orderId);
            Assert.NotNull(completedSaga);
            Assert.Equal(OrderSagaState.Completed, completedSaga!.State);
            Assert.Equal(firstReservationId, Assert.Single(completedSaga.ReservationIds));

            // 3. The order is cancelled: the compensation release is requested for the recorded
            // reservation and the saga is marked as release-requested.
            var orderCancelled = new OrderCancelledIntegrationEvent(
                orderId,
                customerId,
                "ORD-8.8",
                DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = cancellationCorrelationId
            };
            orderCancelledEventId = orderCancelled.EventId;
            var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
            Assert.True(await publisher.PublishAsync(OrderCancelledIntegrationEvent.EventType, orderCancelled));

            await WaitForOrdersProcessedAsync(orderCancelledEventId, OrderCancelledSagaConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            var releaseCommands = await GetOrdersOutboxByCorrelationAsync(cancellationCorrelationId);
            var firstReleaseCommand = Assert.Single(releaseCommands);
            firstReleaseEventId = firstReleaseCommand.EventId;
            Assert.Equal(firstReservationId, JsonSerializer.Deserialize<InventoryReleaseRequestedIntegrationEvent>(firstReleaseCommand.Payload)!.ReservationId);

            // 4. The release is published; the Inventory release consumer releases the first reservation.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForInventoryProcessedAsync(firstReleaseEventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            Assert.Equal(DomainReservationStatus.Released, (await GetReservationAsync(orderId, skuA))!.Status);
            Assert.Equal(0, (await GetItemAsync(skuA))!.ReservedQuantity);
            Assert.Equal(10, (await GetItemAsync(skuA))!.AvailableQuantity);

            // 5. The LATE reservation result (still in flight when the order was cancelled) arrives on the
            // completed saga. Phase 8.8: it is recorded AND compensated immediately — a new release
            // command is staged for it instead of being left to leak until Inventory expiry.
            var lateReserved = CreateReservedEvent(orderId, lateReservationId, skuB, 2, lateReservedCorrelationId);
            lateReservedEventId = lateReserved.EventId;
            await PublishAsync(provider, lateReserved);
            await WaitForOrdersProcessedAsync(lateReservedEventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            var lateReleaseCommands = await GetOrdersOutboxByCorrelationAsync(lateReservedCorrelationId);
            var lateCommand = Assert.Single(lateReleaseCommands);
            lateReleaseEventId = lateCommand.EventId;
            Assert.Equal(InventoryReleaseRequestedIntegrationEvent.EventType, lateCommand.Type);
            Assert.Equal(lateReservedCorrelationId, lateCommand.CorrelationId);
            Assert.Equal(lateReserved.EventId, lateCommand.CausationId);
            Assert.Equal(lateReservationId, JsonSerializer.Deserialize<InventoryReleaseRequestedIntegrationEvent>(lateCommand.Payload)!.ReservationId);

            var finalSaga = await GetSagaAsync(orderId);
            Assert.NotNull(finalSaga);
            Assert.Equal(OrderSagaState.Completed, finalSaga!.State);
            Assert.Equal(2, finalSaga!.ReservationIds.Count);
            Assert.Contains(firstReservationId, finalSaga.ReservationIds);
            Assert.Contains(lateReservationId, finalSaga.ReservationIds);

            // 6. The immediate release is published; the Inventory release consumer releases the late
            // reservation, returning its quantity to stock — the reservation did not leak.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForInventoryProcessedAsync(lateReleaseEventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(DomainReservationStatus.Released, (await GetReservationAsync(orderId, skuB))!.Status);
            Assert.Equal(0, (await GetItemAsync(skuB))!.ReservedQuantity);
            Assert.Equal(5, (await GetItemAsync(skuB))!.AvailableQuantity);
            Assert.Equal(0, (await GetItemAsync(skuA))!.ReservedQuantity);
            Assert.Equal(10, (await GetItemAsync(skuA))!.AvailableQuantity);
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupWithReservationsAsync(
                provider,
                queueName,
                orderId,
                [skuA, skuB],
                [firstReservedEventId, orderCancelledEventId, lateReservedEventId, firstReleaseEventId, lateReleaseEventId],
                [cancellationCorrelationId, lateReservedCorrelationId]);
        }
    }

    private async Task<Guid> SeedSagaAsync(Guid orderId, Guid? correlationId = null)
    {
        var id = correlationId ?? Guid.NewGuid();

        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
        var result = await orchestrator.StartFromOrderCreatedAsync(
            new OrdersOrderId(orderId),
            id,
            causationId: null,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.True(result.SagaCreated);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();

        return id;
    }

    private async Task ApplyReservationSuccessAsync(Guid orderId, Guid reservationId, Guid correlationId)
    {
        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
        var result = await orchestrator.ApplyReservationSuccessAsync(
            new OrdersOrderId(orderId),
            reservationId,
            correlationId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-2));

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
    }

    private static async Task<RabbitMQConsumerBackgroundService> StartConsumerAsync(ServiceProvider provider)
    {
        var consumer = provider.GetServices<IHostedService>()
            .OfType<RabbitMQConsumerBackgroundService>()
            .Single();
        await consumer.StartAsync(CancellationToken.None);
        return consumer;
    }

    private static async Task PublishAsync(IServiceProvider provider, IIntegrationEvent @event, string eventType)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(eventType, @event);
        Assert.True(confirmed);
    }

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservedIntegrationEvent @event) =>
        await PublishAsync(provider, @event, InventoryReservedIntegrationEvent.EventType);

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservationFailedIntegrationEvent @event) =>
        await PublishAsync(provider, @event, InventoryReservationFailedIntegrationEvent.EventType);

    private static async Task WaitForConsumerAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ConsumerCountAsync(provider, queueName) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No consumer connected to queue '{queueName}' within {Timeout}.");
    }

    private async Task WaitForOrdersProcessedAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountOrdersProcessedAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' within {Timeout}.");
    }

    private async Task WaitForInventoryProcessedAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountInventoryProcessedAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' within {Timeout}.");
    }

    private static async Task WaitForQueueEmptyAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GetMessageCountAsync(provider, queueName) == 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Queue '{queueName}' did not drain within {Timeout}.");
    }

    private async Task<OrderSaga?> GetSagaAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSagas
            .AsNoTracking()
            .FirstOrDefaultAsync(saga => saga.OrderId == new OrdersOrderId(orderId));
    }

    private async Task<Order?> GetOrderAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == new OrdersOrderId(orderId));
    }

    private async Task<InventoryReservation?> GetReservationAsync(Guid orderId, string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(reservation =>
                reservation.OrderId == new InventoryOrderId(orderId)
                && reservation.Sku == new InventorySku(sku));
    }

    private async Task<InventoryItem?> GetItemAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == new InventorySku(sku));
    }

    private async Task<int> CountOrdersProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<int> CountInventoryProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetOrdersOutboxByCorrelationAsync(Guid correlationId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CorrelationId == correlationId)
            .ToListAsync();
    }

    private static async Task<uint> ConsumerCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            return await channel.ConsumerCountAsync(queueName, CancellationToken.None);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static async Task<uint> GetMessageCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            var result = await channel.QueueDeclarePassiveAsync(queueName, CancellationToken.None);
            return result.MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static Task DeleteQueueAsync(IServiceProvider provider, string queueName) =>
        Task.Run(async () =>
        {
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await TryDeleteQueueAsync(channel, DeadLetterQueueName(queueName));
            await TryDeleteQueueAsync(channel, queueName);
            try
            {
                await channel.ExchangeDeleteAsync(
                    exchange: DeadLetterExchangeName(queueName),
                    ifUnused: false,
                    noWait: false,
                    cancellationToken: default);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
            }
        });

    private static async Task TryDeleteQueueAsync(IChannel channel, string queueName)
    {
        try
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
        }
    }

    private async Task CleanupAsync(
        IServiceProvider provider,
        string queueName,
        Guid orderId,
        IEnumerable<Guid> correlationIds,
        IEnumerable<Guid> eventIds)
    {
        var keyList = eventIds.Select(id => id.ToString()).ToList();
        var correlationList = correlationIds.ToList();
        var ordersOrderId = new OrdersOrderId(orderId);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            await context.OrderSagas
                .Where(saga => saga.OrderId == ordersOrderId)
                .ExecuteDeleteAsync();
            await context.Orders
                .Where(order => order.Id == ordersOrderId)
                .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            await context.OutboxMessages
                .Where(message => message.Type == OrderCancelledIntegrationEvent.EventType
                    && message.Payload.Contains(orderId.ToString()))
                .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private async Task CleanupWithReservationsAsync(
        IServiceProvider provider,
        string queueName,
        Guid orderId,
        IReadOnlyCollection<string> skus,
        IEnumerable<Guid> eventIds,
        IEnumerable<Guid> correlations)
    {
        var keyList = eventIds.Where(id => id != Guid.Empty).Select(id => id.ToString()).ToList();
        var correlationList = correlations.Where(id => id != Guid.Empty).ToList();
        var ordersOrderId = new OrdersOrderId(orderId);
        var inventoryOrderId = new InventoryOrderId(orderId);

        using (var ordersScope = _fixture.Services.CreateScope())
        {
            var context = ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await context.OrderSagas
                .Where(saga => saga.OrderId == ordersOrderId)
                .ExecuteDeleteAsync();
            await context.Orders
                .Where(order => order.Id == ordersOrderId)
                .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
        }

        using (var inventoryScope = _fixture.Services.CreateScope())
        {
            var context = inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.InventoryReservations
                .Where(reservation => reservation.OrderId == inventoryOrderId)
                .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            if (skus.Count > 0)
            {
                var skuValues = skus.Select(sku => new InventorySku(sku)).ToList();
                await context.InventoryItems
                    .Where(item => skuValues.Contains(item.Id))
                    .ExecuteDeleteAsync();
            }
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.recovery.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];

    private static InventoryReservedIntegrationEvent CreateReservedEvent(Guid orderId) =>
        CreateReservedEvent(orderId, Guid.NewGuid(), "SKU-8.8", 3);

    private static InventoryReservedIntegrationEvent CreateReservedEvent(
        Guid orderId,
        Guid reservationId,
        string sku,
        int quantity,
        Guid? correlationId = null,
        Guid? causationId = null) =>
        new(
            reservationId,
            orderId,
            sku,
            quantity,
            DateTimeOffset.UtcNow.AddMinutes(30),
            ContractReservationStatus.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1)
        {
            CorrelationId = correlationId ?? Guid.NewGuid(),
            CausationId = causationId ?? Guid.NewGuid()
        };

    private static InventoryReservationFailedIntegrationEvent CreateFailedEvent(Guid orderId) =>
        new(orderId, "SKU-8.8", 3, "Inventory.InsufficientStock")
        {
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid()
        };
}