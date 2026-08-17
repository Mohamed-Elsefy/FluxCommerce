using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class OrderSummaryProjectionServiceTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProjectAsync_ExistingOrder_LoadsByIdAndStagesProjectionWithTraceIdentifiers()
    {
        var customerId = CustomerId.Create();
        var orderId = OrderId.Create();
        var order = OrderTestData.CreateOrder(orderId, customerId, CreatedAtUtc);
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var orderStore = new Mock<IOrderStore>();
        orderStore
            .Setup(s => s.GetByIdAsync(It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        OrderSummaryProjection? staged = null;
        var projectionStore = new Mock<IOrderSummaryProjectionStore>();
        projectionStore
            .Setup(s => s.Add(It.IsAny<OrderSummaryProjection>()))
            .Callback<OrderSummaryProjection>(projection => staged = projection);
        var service = new OrderSummaryProjectionService(orderStore.Object, projectionStore.Object);

        await service.ProjectAsync(orderId.Value, correlationId, causationId, CancellationToken.None);

        orderStore.Verify(s => s.GetByIdAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(staged);
        Assert.Equal(orderId.Value, staged!.OrderId);
        Assert.Equal(order.OrderNumber.Value, staged.OrderNumber);
        Assert.Equal(customerId.Value, staged.CustomerId);
        Assert.Equal("Pending", staged.Status);
        Assert.Equal(25.00m, staged.GrandTotalAmount);
        Assert.Equal("USD", staged.GrandTotalCurrency);
        Assert.Equal(2, staged.ItemCount);
        Assert.Equal(correlationId, staged.CorrelationId);
        Assert.Equal(causationId, staged.CausationId);
        Assert.Equal(CreatedAtUtc, staged.CreatedAtUtc);
        Assert.True(staged.ProjectedAtUtc >= CreatedAtUtc);
    }

    [Fact]
    public async Task ProjectAsync_MissingOrder_ThrowsRetryableInvalidOperation_AndStagesNothing()
    {
        var orderStore = new Mock<IOrderStore>();
        orderStore
            .Setup(s => s.GetByIdAsync(It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        var projectionStore = new Mock<IOrderSummaryProjectionStore>();
        var service = new OrderSummaryProjectionService(orderStore.Object, projectionStore.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProjectAsync(OrderId.Create().Value, Guid.NewGuid(), null, CancellationToken.None));

        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        projectionStore.Verify(s => s.Add(It.IsAny<OrderSummaryProjection>()), Times.Never);
    }

    [Fact]
    public async Task ProjectAsync_NullCausation_IsProjectedWithNullCausation()
    {
        var order = OrderTestData.CreateOrder(OrderId.Create(), CustomerId.Create(), CreatedAtUtc);
        var correlationId = Guid.NewGuid();

        var orderStore = new Mock<IOrderStore>();
        orderStore
            .Setup(s => s.GetByIdAsync(It.IsAny<OrderId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        OrderSummaryProjection? staged = null;
        var projectionStore = new Mock<IOrderSummaryProjectionStore>();
        projectionStore
            .Setup(s => s.Add(It.IsAny<OrderSummaryProjection>()))
            .Callback<OrderSummaryProjection>(projection => staged = projection);
        var service = new OrderSummaryProjectionService(orderStore.Object, projectionStore.Object);

        await service.ProjectAsync(order.Id.Value, correlationId, causationId: null, CancellationToken.None);

        Assert.NotNull(staged);
        Assert.Equal(correlationId, staged!.CorrelationId);
        Assert.Null(staged.CausationId);
    }
}