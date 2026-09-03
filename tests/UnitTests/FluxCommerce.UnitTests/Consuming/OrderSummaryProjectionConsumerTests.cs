using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

public class OrderSummaryProjectionConsumerTests
{
    [Fact]
    public void HandlerName_IsStableAndScopedToTheModule()
    {
        var consumer = new OrderSummaryProjectionConsumer(Mock.Of<IOrderSummaryProjectionService>());

        Assert.Equal("Orders.ProjectOrderSummary", consumer.HandlerName);
    }

    [Fact]
    public async Task HandleAsync_DelegatesToProjectionService_WithEventAndEnvelopeTraceIdentifiers()
    {
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var @event = new OrderCreatedIntegrationEvent(
            orderId,
            Guid.NewGuid(),
            "ORD-TEST",
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero))
        {
            EventId = eventId,
            CorrelationId = correlationId,
            CausationId = causationId
        };
        var envelope = IntegrationEventEnvelope.From(@event, OrderCreatedIntegrationEvent.EventType);

        var projectionService = new Mock<IOrderSummaryProjectionService>();
        var consumer = new OrderSummaryProjectionConsumer(projectionService.Object);

        await consumer.HandleAsync(@event, envelope, CancellationToken.None);

        projectionService.Verify(
            s => s.ProjectAsync(orderId, correlationId, causationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutCausation_DelegatesNullCausation()
    {
        var correlationId = Guid.NewGuid();
        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-TEST",
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = correlationId,
            CausationId = null
        };
        var envelope = IntegrationEventEnvelope.From(@event, OrderCreatedIntegrationEvent.EventType);

        var projectionService = new Mock<IOrderSummaryProjectionService>();
        var consumer = new OrderSummaryProjectionConsumer(projectionService.Object);

        await consumer.HandleAsync(@event, envelope, CancellationToken.None);

        projectionService.Verify(
            s => s.ProjectAsync(@event.OrderId, correlationId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}