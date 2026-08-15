using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
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
/// Phase 6.7: the Orders outbox dispatcher against the real SQL Server engine and the real RabbitMQ
/// broker. A dispatch cycle publishes each claimed message to the dedicated test exchange (routing
/// key = event type, body = the deserialized payload) and marks it Published; a failed publication
/// returns the message to Pending with a bumped retry count so a later cycle re-publishes it (never
/// lost); stale InProgress claims are recovered before claiming; and concurrent dispatchers publish
/// every message exactly once.
/// </summary>
[Collection(SqlServerOrdersDispatcherTestCollection.Name)]
public class OrdersOutboxDispatcherIntegrationTests
{
    private readonly OrdersOutboxDispatcherTestFixture _fixture;

    public OrdersOutboxDispatcherIntegrationTests(OrdersOutboxDispatcherTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dispatch_DispatchingPendingMessages_PublishesEachAndMarksPublished()
    {
        var events = Enumerable.Range(0, 3)
            .Select(index => CreateOrderCreated($"ORD-D{index}"))
            .ToList();
        var ids = await SeedAsync(events);

        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            using var verifyScope = _fixture.Services.CreateScope();
            var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .ToListAsync();

            Assert.Equal(ids.Count, loaded.Count);
            Assert.All(loaded, message => Assert.Equal(OutboxStatus.Published, message.Status));

            var delivered = await ReceiveAsync(channel, queueName, events.Count);

            Assert.Equal(events.Count, delivered.Count);
            Assert.All(delivered, message =>
            {
                Assert.Equal(OrderCreatedIntegrationEvent.EventType, message.RoutingKey);
                Assert.Equal(OrdersOutboxDispatcherTestFixture.TestExchangeName, message.Exchange);
            });
            Assert.Equal(
                events.OrderBy(@event => @event.OrderNumber).ToList(),
                delivered.Select(message => message.Event).OrderBy(@event => @event.OrderNumber).ToList());
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task Dispatch_FailedPublish_ReturnsToPending_ThenRetryPublishes()
    {
        var @event = CreateOrderCreated("ORD-FAIL");
        var ids = await SeedAsync([@event]);

        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel);

        try
        {
            // Reports one failure (broker nack) and then delegates to the real broker, so the retry
            // leg verifies an actual delivery rather than a fake one.
            var failOncePublisher = new FailOncePublisher(
                _fixture.Services.GetRequiredService<IRabbitMQPublisher>());

            using var dispatcherProvider = BuildDispatcherProvider(failOncePublisher);
            using var scope = dispatcherProvider.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            using (var verifyScope = _fixture.Services.CreateScope())
            {
                var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                var loaded = await context.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(message => message.Id == ids.Single());

                Assert.Equal(OutboxStatus.Pending, loaded.Status);
                Assert.Equal(1, loaded.RetryCount);
                Assert.NotNull(loaded.Error);
                Assert.Null(loaded.ProcessedAtUtc);
            }

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            using (var verifyScope = _fixture.Services.CreateScope())
            {
                var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                var loaded = await context.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(message => message.Id == ids.Single());

                Assert.Equal(OutboxStatus.Published, loaded.Status);
            }

            var delivered = await ReceiveAsync(channel, queueName, 1);
            Assert.Single(delivered);
            Assert.Equal(@event.OrderNumber, delivered[0].Event.OrderNumber);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task Dispatch_RecoversStaleInProgressClaim_ThenPublishes()
    {
        var @event = CreateOrderCreated("ORD-STALE");
        var ids = await SeedAsync(
            [@event],
            OutboxStatus.InProgress,
            claimedAtUtc: DateTime.UtcNow.AddMinutes(-10));

        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel);

        try
        {
            using var scope = _fixture.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            using var verifyScope = _fixture.Services.CreateScope();
            var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .SingleAsync(message => message.Id == ids.Single());

            Assert.Equal(OutboxStatus.Published, loaded.Status);

            var delivered = await ReceiveAsync(channel, queueName, 1);
            Assert.Single(delivered);
            Assert.Equal(@event.OrderNumber, delivered[0].Event.OrderNumber);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(ids);
        }
    }

    [Fact]
    public async Task ConcurrentDispatches_PublishEachMessageExactlyOnce()
    {
        var events = Enumerable.Range(0, 20)
            .Select(index => CreateOrderCreated($"ORD-C{index:D2}"))
            .ToList();
        var ids = await SeedAsync(events);

        await using var channel = await _fixture.Services
            .GetRequiredService<IRabbitMQConnectionProvider>()
            .CreateChannelAsync();
        var queueName = await DeclareQueueAsync(channel);

        try
        {
            // Two independent dispatchers race over the same queue, each with its own scope and
            // DbContext. Atomic claiming guarantees each message is claimed exactly once, so the
            // broker must receive each event exactly once.
            var tasks = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(async () =>
                {
                    using var scope = _fixture.Services.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                    await dispatcher.DispatchPendingAsync(CancellationToken.None);
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            using var verifyScope = _fixture.Services.CreateScope();
            var context = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var loaded = await context.OutboxMessages
                .AsNoTracking()
                .Where(message => ids.Contains(message.Id))
                .ToListAsync();

            Assert.Equal(ids.Count, loaded.Count);
            Assert.All(loaded, message => Assert.Equal(OutboxStatus.Published, message.Status));

            var delivered = await ReceiveAsync(channel, queueName, events.Count);
            Assert.Equal(events.Count, delivered.Count);
            Assert.Equal(
                events.OrderBy(@event => @event.OrderNumber).ToList(),
                delivered.Select(message => message.Event).OrderBy(@event => @event.OrderNumber).ToList());
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
            await CleanupAsync(ids);
        }
    }

    private async Task<List<Guid>> SeedAsync(
        IReadOnlyCollection<OrderCreatedIntegrationEvent> events,
        OutboxStatus status = OutboxStatus.Pending,
        DateTime? claimedAtUtc = null)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // Leftover rows from earlier tests (or from other Orders collections) must not leak into a
        // test's claim; clear the table so the test observes only its own rows.
        await context.OutboxMessages.ExecuteDeleteAsync();

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var index = 0;
        var messages = events
            .Select(@event =>
            {
                var message = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventId = @event.EventId,
                    Type = OrderCreatedIntegrationEvent.EventType,
                    Payload = JsonSerializer.Serialize(@event),
                    CorrelationId = @event.CorrelationId,
                    CausationId = @event.CausationId,
                    OccurredAtUtc = @event.OccurredAtUtc,
                    CreatedAtUtc = baseTime.AddMinutes(index),
                    ProcessedAtUtc = status == OutboxStatus.Pending ? null : (claimedAtUtc ?? DateTime.UtcNow),
                    Status = status,
                    RetryCount = status == OutboxStatus.Pending ? 0 : 1,
                    Error = null
                };
                index++;
                return message;
            })
            .ToList();

        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();

        return messages.Select(message => message.Id).ToList();
    }

    private async Task CleanupAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return;

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await context.OutboxMessages
            .Where(message => idList.Contains(message.Id))
            .ExecuteDeleteAsync();
    }

    private static ServiceProvider BuildDispatcherProvider(IRabbitMQPublisher publisher)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddOrdersInfrastructure(OrdersOutboxDispatcherTestFixture.ConnectionString);

        services.AddLogging();
        services.AddSingleton<IRabbitMQPublisher>(publisher);

        services.Configure<OutboxDispatchOptions>(options => options.ClaimTimeout = TimeSpan.FromSeconds(5));

        return services.BuildServiceProvider();
    }

    private static async Task<string> DeclareQueueAsync(IChannel channel)
    {
        var queueName = $"it.dq.{Guid.NewGuid():N}";

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
            routingKey: OrderCreatedIntegrationEvent.EventType,
            arguments: null!,
            noWait: false,
            cancellationToken: default);

        return queueName;
    }

    private static async Task<List<(string RoutingKey, string Exchange, OrderCreatedIntegrationEvent Event)>> ReceiveAsync(
        IChannel channel,
        string queueName,
        int expectedCount)
    {
        var delivered = new List<(string RoutingKey, string Exchange, OrderCreatedIntegrationEvent Event)>();

        for (var attempt = 0; attempt < 100 && delivered.Count < expectedCount; attempt++)
        {
            var result = await channel.BasicGetAsync(queue: queueName, autoAck: true, cancellationToken: default);

            if (result is not null)
            {
                delivered.Add((
                    result.RoutingKey,
                    result.Exchange,
                    JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(result.Body.Span)!));
            }
            else
            {
                await Task.Delay(100);
            }
        }

        return delivered;
    }

    private static OrderCreatedIntegrationEvent CreateOrderCreated(string orderNumber) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            orderNumber,
            new DateTimeOffset(2026, 8, 14, 15, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd")
        };

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

        public bool IsAuthenticated => false;
    }
}