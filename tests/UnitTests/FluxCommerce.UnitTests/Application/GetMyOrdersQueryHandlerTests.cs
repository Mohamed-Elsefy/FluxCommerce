using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetMyOrdersQueryHandlerTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_WithoutQuerying()
    {
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var store = new Mock<IOrderStore>();
        var handler = new GetMyOrdersQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetMyOrdersQuery(null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Order.QueryAuthenticationRequired", result.Error.Code);
        store.Verify(
            s => s.GetMyOrdersPageAsync(It.IsAny<CustomerId>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OrderStatus?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DefaultPagination_QueriesPageOneWithDefaultSize()
    {
        var currentUser = CreateUser();
        CustomerId? requestedCustomerId = null;
        int? requestedPageNumber = null;
        int? requestedPageSize = null;
        var store = CreateStore(result: null);
        store
            .Setup(s => s.GetMyOrdersPageAsync(
                It.IsAny<CustomerId>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<OrderStatus?>(),
                It.IsAny<CancellationToken>()))
            .Callback<CustomerId, int, int, OrderStatus?, CancellationToken>((customerId, pageNumber, pageSize, _, _) =>
            {
                requestedCustomerId = customerId;
                requestedPageNumber = pageNumber;
                requestedPageSize = pageSize;
            })
            .ReturnsAsync(EmptyPage(1, 20));

        var handler = new GetMyOrdersQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetMyOrdersQuery(null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentUser.UserId, requestedCustomerId!.Value);
        Assert.Equal(1, requestedPageNumber);
        Assert.Equal(20, requestedPageSize);
    }

    [Fact]
    public async Task HandleAsync_PageBelowOne_ReturnsValidationError_WithoutQuerying()
    {
        var handler = CreateHandler(out var store);

        var result = await handler.HandleAsync(new GetMyOrdersQuery(0, 20), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Order.ValidationFailed", result.Error.Code);
        store.Verify(
            s => s.GetMyOrdersPageAsync(It.IsAny<CustomerId>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OrderStatus?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task HandleAsync_InvalidPageSize_ReturnsValidationError_WithoutQuerying(int pageSize)
    {
        var handler = CreateHandler(out var store);

        var result = await handler.HandleAsync(new GetMyOrdersQuery(1, pageSize), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.ValidationFailed", result.Error.Code);
        store.Verify(
            s => s.GetMyOrdersPageAsync(It.IsAny<CustomerId>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OrderStatus?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OrdersFound_MapsToSummariesAndPreservesPaging()
    {
        var customerId = CustomerId.Create();
        var currentUser = new FakeCurrentUser { UserId = customerId.Value, IsAuthenticated = true };
        var firstOrderId = OrderId.Create();
        var store = CreateStore(new PagedResult<OrderSummaryReadModel>(
            [
                new OrderSummaryReadModel(
                    firstOrderId,
                    new OrderNumber("ORD-002"),
                    OrderStatus.Pending,
                    49.00m,
                    "USD",
                    2,
                    CreatedAtUtc),
                new OrderSummaryReadModel(
                    OrderId.Create(),
                    new OrderNumber("ORD-001"),
                    OrderStatus.Cancelled,
                    9.50m,
                    "USD",
                    1,
                    CreatedAtUtc.AddMinutes(-5)),
            ],
            7,
            1,
            20));
        var handler = new GetMyOrdersQueryHandler(currentUser, store.Object);

        var result = await handler.HandleAsync(new GetMyOrdersQuery(1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var page = result.Value!;
        var items = page.Items.ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(firstOrderId.Value, items[0].OrderId);
        Assert.Equal("ORD-002", items[0].OrderNumber);
        Assert.Equal("Pending", items[0].Status);
        Assert.Equal(49.00m, items[0].GrandTotal.Amount);
        Assert.Equal("USD", items[0].GrandTotal.Currency);
        Assert.Equal(2, items[0].ItemCount);
        Assert.Equal("Cancelled", items[1].Status);
    }

    private static GetMyOrdersQueryHandler CreateHandler(out Mock<IOrderStore> store)
    {
        store = CreateStore(result: null);
        return new GetMyOrdersQueryHandler(CreateUser(), store.Object);
    }

    private static FakeCurrentUser CreateUser() =>
        new() { UserId = CustomerId.Create().Value, IsAuthenticated = true };

    private static Mock<IOrderStore> CreateStore(PagedResult<OrderSummaryReadModel>? result)
    {
        var store = new Mock<IOrderStore>();
        store
            .Setup(s => s.GetMyOrdersPageAsync(
                It.IsAny<CustomerId>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<OrderStatus?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? EmptyPage(1, 20));
        return store;
    }

    private static PagedResult<OrderSummaryReadModel> EmptyPage(int pageNumber, int pageSize) =>
        new([], 0, pageNumber, pageSize);
}
