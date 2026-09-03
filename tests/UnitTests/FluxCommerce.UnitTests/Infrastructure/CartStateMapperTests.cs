using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CartStateMapperTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ToDocument_MapsAllCartFields()
    {
        var mapper = new CartStateMapper();
        var state = CreateState();

        var document = mapper.ToDocument(state);

        Assert.Equal(state.CartId.Value, document.CartId);
        Assert.Equal(state.UserId, document.UserId);
        Assert.Equal(state.Version, document.Version);
        Assert.Equal(state.CreatedAtUtc, document.CreatedAtUtc);
        Assert.Equal(state.UpdatedAtUtc, document.UpdatedAtUtc);
        Assert.Equal(state.Items.Count, document.Items.Count);
    }

    [Fact]
    public void ToDocument_MapsAllItemFields()
    {
        var mapper = new CartStateMapper();
        var state = CreateState();

        var document = mapper.ToDocument(state);
        var item = document.Items[0];

        Assert.Equal(state.Items[0].ProductId.Value, item.ProductId);
        Assert.Equal(state.Items[0].VariantId.Value, item.VariantId);
        Assert.Equal(state.Items[0].Sku.Value, item.Sku);
        Assert.Equal(state.Items[0].ProductName, item.ProductName);
        Assert.Equal(state.Items[0].UnitPrice.Amount, item.UnitPrice);
        Assert.Equal(state.Items[0].UnitPrice.Currency, item.Currency);
        Assert.Equal(state.Items[0].Quantity, item.Quantity);
    }

    [Fact]
    public void ToState_MapsAllCartFields()
    {
        var mapper = new CartStateMapper();
        var document = CreateDocument();

        var state = mapper.ToState(document);

        Assert.Equal(new CartId(document.CartId), state.CartId);
        Assert.Equal(document.UserId, state.UserId);
        Assert.Equal(document.Version, state.Version);
        Assert.Equal(document.CreatedAtUtc, state.CreatedAtUtc);
        Assert.Equal(document.UpdatedAtUtc, state.UpdatedAtUtc);
        Assert.Equal(document.Items.Count, state.Items.Count);
    }

    [Fact]
    public void ToState_MapsAllItemFields()
    {
        var mapper = new CartStateMapper();
        var document = CreateDocument();

        var state = mapper.ToState(document);
        var item = state.Items[0];

        Assert.Equal(new ProductId(document.Items[0].ProductId), item.ProductId);
        Assert.Equal(new VariantId(document.Items[0].VariantId), item.VariantId);
        Assert.Equal(new Sku(document.Items[0].Sku), item.Sku);
        Assert.Equal(document.Items[0].ProductName, item.ProductName);
        Assert.Equal(new Money(document.Items[0].UnitPrice, document.Items[0].Currency), item.UnitPrice);
        Assert.Equal(document.Items[0].Quantity, item.Quantity);
    }

    [Fact]
    public void ToDocument_Then_ToState_RoundTripsState()
    {
        var mapper = new CartStateMapper();
        var state = CreateState();

        var roundTripped = mapper.ToState(mapper.ToDocument(state));

        AssertCartStateEqual(state, roundTripped);
    }

    [Fact]
    public void ToDocument_Then_ToState_RoundTripsGuestCart()
    {
        var mapper = new CartStateMapper();
        var state = CreateState(userId: null);

        var roundTripped = mapper.ToState(mapper.ToDocument(state));

        AssertCartStateEqual(state, roundTripped);
        Assert.Null(roundTripped.UserId);
    }

    [Fact]
    public void ToDocument_Then_ToState_RoundTripsEmptyCart()
    {
        var mapper = new CartStateMapper();
        var state = CreateState() with { Items = [] };

        var roundTripped = mapper.ToState(mapper.ToDocument(state));

        AssertCartStateEqual(state, roundTripped);
        Assert.Empty(roundTripped.Items);
    }

    [Fact]
    public void ToDocument_Serialize_Deserialize_ToState_RoundTripsState()
    {
        var mapper = new CartStateMapper();
        var serializer = new CartStateSerializer();
        var state = CreateState();

        var document = serializer.Deserialize(serializer.Serialize(mapper.ToDocument(state)));
        var roundTripped = mapper.ToState(document);

        AssertCartStateEqual(state, roundTripped);
    }

    [Fact]
    public void ToDocument_NullState_Throws()
    {
        var mapper = new CartStateMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.ToDocument(null!));
    }

    [Fact]
    public void ToState_NullDocument_Throws()
    {
        var mapper = new CartStateMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.ToState(null!));
    }

    private static void AssertCartStateEqual(CartState expected, CartState actual)
    {
        Assert.Equal(expected.CartId, actual.CartId);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.Items, actual.Items);
    }

    private static CartState CreateState(Guid? userId = null) =>
        new(
            CartId: new CartId(Guid.NewGuid()),
            UserId: userId,
            Version: 3,
            CreatedAtUtc: FixedTime,
            UpdatedAtUtc: FixedTime.AddMinutes(5),
            Items:
            [
                new CartItemState(
                    ProductId: new ProductId(Guid.NewGuid()),
                    VariantId: new VariantId(Guid.NewGuid()),
                    Sku: new Sku("SKU-1001"),
                    ProductName: "Wireless Mouse",
                    UnitPrice: new Money(24.99m, "USD"),
                    Quantity: 2),
            ]);

    private static CartDocument CreateDocument() =>
        new(
            CartId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            Version: 7,
            CreatedAtUtc: FixedTime,
            UpdatedAtUtc: FixedTime.AddMinutes(5),
            Items:
            [
                new CartItemDocument(
                    ProductId: Guid.NewGuid(),
                    VariantId: Guid.NewGuid(),
                    Sku: "SKU-1002",
                    ProductName: "Mechanical Keyboard",
                    UnitPrice: 89.00m,
                    Currency: "USD",
                    Quantity: 1),
            ]);
}

