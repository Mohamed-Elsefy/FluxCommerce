using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class OrderResponseMapperTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToDetails_MapsAllOrderProperties()
    {
        var order = OrderTestData.CreateOrder(OrderId.Create(), CustomerId.Create(), CreatedAtUtc);

        var details = OrderResponseMapper.ToDetails(order);

        Assert.Equal(order.Id.Value, details.OrderId);
        Assert.Equal(order.OrderNumber.Value, details.OrderNumber);
        Assert.Equal("Pending", details.Status);
        Assert.Equal(order.Subtotal.Amount, details.Subtotal.Amount);
        Assert.Equal(order.ShippingCost.Amount, details.ShippingCost.Amount);
        Assert.Equal(order.Discount.Amount, details.Discount.Amount);
        Assert.Equal(order.GrandTotal.Amount, details.GrandTotal.Amount);
        Assert.Equal(order.GrandTotal.Currency, details.GrandTotal.Currency);
        Assert.Equal(2, details.ItemCount);
        Assert.Equal(order.CreatedAtUtc, details.CreatedAtUtc);

        Assert.Equal("ORD-SUMMARY", details.ShippingAddress.RecipientName);
        Assert.Equal("1 Main St", details.ShippingAddress.Line1);
        Assert.Equal("SW1A 1AA", details.ShippingAddress.PostalCode);
        Assert.Equal("GB", details.ShippingAddress.CountryCode);
        Assert.Equal(details.ShippingAddress, details.BillingAddress);

        Assert.Equal(2, details.LineItems.Count);
        var line = details.LineItems[0];
        Assert.Equal("Wireless Mouse", line.ProductName);
        Assert.Equal("SKU-1", line.Sku);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(15.00m, line.UnitPrice);
        Assert.Equal("USD", line.UnitPriceCurrency);
        Assert.Equal(15.00m, line.LineTotal);
        Assert.Equal("USD", line.LineTotalCurrency);
    }

    [Fact]
    public void ToSummary_MapsReadModel()
    {
        var orderId = OrderId.Create();
        var model = new OrderSummaryReadModel(
            orderId,
            new OrderNumber("ORD-1001"),
            OrderStatus.Cancelled,
            42.00m,
            "USD",
            3,
            CreatedAtUtc);

        var summary = OrderResponseMapper.ToSummary(model);

        Assert.Equal(orderId.Value, summary.OrderId);
        Assert.Equal("ORD-1001", summary.OrderNumber);
        Assert.Equal("Cancelled", summary.Status);
        Assert.Equal(42.00m, summary.GrandTotal.Amount);
        Assert.Equal("USD", summary.GrandTotal.Currency);
        Assert.Equal(3, summary.ItemCount);
        Assert.Equal(CreatedAtUtc, summary.CreatedAtUtc);
    }

    [Fact]
    public void ToSummaries_MapsEachReadModel()
    {
        var models = new[]
        {
            new OrderSummaryReadModel(OrderId.Create(), new OrderNumber("ORD-1"), OrderStatus.Pending, 1m, "USD", 1, CreatedAtUtc),
            new OrderSummaryReadModel(OrderId.Create(), new OrderNumber("ORD-2"), OrderStatus.Pending, 2m, "USD", 2, CreatedAtUtc),
        };

        var summaries = OrderResponseMapper.ToSummaries(models);

        Assert.Equal(2, summaries.Count);
        Assert.Equal("ORD-1", summaries[0].OrderNumber);
        Assert.Equal("ORD-2", summaries[1].OrderNumber);
    }
}
