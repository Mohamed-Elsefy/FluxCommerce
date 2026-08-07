using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class RedisCartTestData
{
    public static readonly DateTimeOffset FixedTime = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    public static CartState Create(Guid? userId = null, long version = 1, int quantity = 2, decimal unitPrice = 24.99m) =>
        new(
            CartId: new CartId(Guid.NewGuid()),
            UserId: userId,
            Version: version,
            CreatedAtUtc: FixedTime,
            UpdatedAtUtc: FixedTime.AddMinutes(5),
            Items:
            [
                new CartItemState(
                    ProductId: new ProductId(Guid.NewGuid()),
                    VariantId: new VariantId(Guid.NewGuid()),
                    Sku: new Sku("SKU-1001"),
                    ProductName: "Wireless Mouse",
                    UnitPrice: new Money(unitPrice, "USD"),
                    Quantity: quantity),
            ]);

    public static void AssertCartStateEqual(CartState expected, CartState? actual)
    {
        Xunit.Assert.NotNull(actual);
        Xunit.Assert.Equal(expected.CartId, actual!.CartId);
        Xunit.Assert.Equal(expected.UserId, actual.UserId);
        Xunit.Assert.Equal(expected.Version, actual.Version);
        Xunit.Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Xunit.Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Xunit.Assert.Equal(expected.Items, actual.Items);
    }
}
