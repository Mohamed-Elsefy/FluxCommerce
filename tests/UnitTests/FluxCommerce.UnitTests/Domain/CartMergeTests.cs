using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartMergeTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static Cart CreateGuestCart() => Cart.CreateGuest(CartId.Create(), UtcNow);

    private static Cart CreateAuthenticatedCart(out Guid userId)
    {
        userId = Guid.NewGuid();
        return Cart.CreateAuthenticated(CartId.Create(), userId, UtcNow);
    }

    private static void AddItem(
        Cart cart,
        VariantId variantId,
        int quantity = 1,
        string sku = "SKU-A",
        string productName = "Product A",
        Money? unitPrice = null)
    {
        var result = cart.AddItem(
            ProductId.Create(),
            variantId,
            new Sku(sku),
            productName,
            unitPrice ?? new Money(19.99m, "USD"),
            quantity,
            UtcNow);

        Assert.True(result.IsSuccess, result.Error.Code);
    }

    [Fact]
    public void Merge_EmptyGuestSource_PopulatedTarget_TargetUnchanged()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var variant = VariantId.Create();
        AddItem(target, variant, quantity: 2);
        var targetVersion = target.Version;

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(target.Items);
        Assert.Equal(2, Assert.Single(target.Items).Quantity);
        Assert.Equal(targetVersion, target.Version);
    }

    [Fact]
    public void Merge_PopulatedGuestSource_EmptyTarget_AddsAllSourceItems()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var variantA = VariantId.Create();
        var variantB = VariantId.Create();
        AddItem(source, variantA, quantity: 1);
        AddItem(source, variantB, quantity: 2);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, target.Items.Count);
        Assert.Contains(target.Items, item => item.VariantId == variantA);
        Assert.Contains(target.Items, item => item.VariantId == variantB);
    }

    [Fact]
    public void Merge_BothCartsPopulated_CombinesItems()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var targetVariant = VariantId.Create();
        var sourceVariantA = VariantId.Create();
        var sourceVariantB = VariantId.Create();
        AddItem(target, targetVariant, quantity: 1);
        AddItem(source, sourceVariantA, quantity: 1);
        AddItem(source, sourceVariantB, quantity: 2);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, target.Items.Count);
    }

    [Fact]
    public void Merge_SourceOnlyVariant_IsAddedToTarget()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var sourceOnly = VariantId.Create();
        AddItem(source, sourceOnly, quantity: 3, sku: "SKU-X", productName: "Product X", unitPrice: new Money(7.5m, "EUR"));

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(target.Items);
        Assert.Equal(sourceOnly, item.VariantId);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(new Sku("SKU-X"), item.Sku);
        Assert.Equal("Product X", item.ProductName);
        Assert.Equal(new Money(7.5m, "EUR"), item.UnitPrice);
    }

    [Fact]
    public void Merge_NoDuplicateItemsCreated()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        var sourceOnly = VariantId.Create();
        AddItem(target, shared, quantity: 1);
        AddItem(source, shared, quantity: 1);
        AddItem(source, sourceOnly, quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, target.Items.Count);
        Assert.Equal(2, target.Items.Select(item => item.VariantId).Distinct().Count());
    }

    [Fact]
    public void Merge_SameVariantInBothCarts_CombinesQuantities()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 3);
        AddItem(source, shared, quantity: 2);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(target.Items);
        Assert.Equal(5, Assert.Single(target.Items).Quantity);
    }

    [Fact]
    public void Merge_DuplicateVariant_KeepsSingleCartItem()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 1);
        AddItem(source, shared, quantity: 4);

        target.Merge(source, UtcNow);

        Assert.Single(target.Items);
    }

    [Fact]
    public void Merge_DuplicateVariant_TargetSnapshotRemainsAuthoritative()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 1, sku: "SKU-T", productName: "Target Product", unitPrice: new Money(29.99m, "USD"));
        AddItem(source, shared, quantity: 1, sku: "SKU-S", productName: "Source Product", unitPrice: new Money(9.99m, "EUR"));

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(target.Items);
        Assert.Equal(new Sku("SKU-T"), item.Sku);
        Assert.Equal("Target Product", item.ProductName);
        Assert.Equal(new Money(29.99m, "USD"), item.UnitPrice);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public void Merge_WithinQuantityLimit_Succeeds()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: int.MaxValue - 1);
        AddItem(source, shared, quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(int.MaxValue, Assert.Single(target.Items).Quantity);
    }

    [Fact]
    public void Merge_ExceedingQuantityLimit_ReturnsFailure()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 1);
        AddItem(source, shared, quantity: int.MaxValue);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.QuantityLimitExceeded, result.Error);
    }

    [Fact]
    public void Merge_ExceedingQuantityLimit_DoesNotCorruptEitherCart()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 1);
        AddItem(source, shared, quantity: int.MaxValue);
        var targetVersion = target.Version;
        var sourceVersion = source.Version;

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(1, Assert.Single(target.Items).Quantity);
        Assert.Equal(int.MaxValue, Assert.Single(source.Items).Quantity);
        Assert.Equal(targetVersion, target.Version);
        Assert.Equal(sourceVersion, source.Version);
    }

    [Fact]
    public void Merge_ExceedingLimit_RejectsBeforeApplyingAnyItems()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var overflowing = VariantId.Create();
        var sourceOnly = VariantId.Create();
        AddItem(target, overflowing, quantity: 1);
        AddItem(source, overflowing, quantity: int.MaxValue);
        AddItem(source, sourceOnly, quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(1, Assert.Single(target.Items).Quantity);
        Assert.DoesNotContain(target.Items, item => item.VariantId == sourceOnly);
        Assert.Equal(2, source.Items.Count);
    }

    [Fact]
    public void Merge_GuestSource_AuthenticatedTarget_Succeeds()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(target.Items);
    }

    [Fact]
    public void Merge_TargetOwnershipRemainsUnchanged()
    {
        var target = CreateAuthenticatedCart(out var userId);
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(target.IsAuthenticated);
        Assert.False(target.IsGuest);
        Assert.Equal(userId, target.UserId);
    }

    [Fact]
    public void Merge_AuthenticatedSource_ReturnsFailure()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateAuthenticatedCart(out _);
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CartErrors.InvalidMergeSource, result.Error);
    }

    [Fact]
    public void Merge_AuthenticatedSource_DoesNotMutateEitherCart()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateAuthenticatedCart(out _);
        var sourceVariant = VariantId.Create();
        AddItem(source, sourceVariant, quantity: 1);
        var targetVersion = target.Version;
        var sourceVersion = source.Version;

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Empty(target.Items);
        Assert.Equal(1, Assert.Single(source.Items).Quantity);
        Assert.Equal(targetVersion, target.Version);
        Assert.Equal(sourceVersion, source.Version);
    }

    [Fact]
    public void Merge_Success_EmptiesSourceCart()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);
        AddItem(source, VariantId.Create(), quantity: 2);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Empty(source.Items);
    }

    [Fact]
    public void Merge_Success_SourceCartIdentityRemainsValid()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var sourceId = source.Id;
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(sourceId, source.Id);
        Assert.True(source.IsGuest);
        Assert.Empty(source.Items);
    }

    [Fact]
    public void Merge_Success_TargetCartIdUnchanged()
    {
        var target = CreateAuthenticatedCart(out _);
        var targetId = target.Id;
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetId, target.Id);
    }

    [Fact]
    public void Merge_Success_AdvancesTargetVersionByOne()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);
        var targetVersion = target.Version;

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetVersion + 1, target.Version);
    }

    [Fact]
    public void Merge_Success_UpdatesTargetUpdatedAt()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        AddItem(source, VariantId.Create(), quantity: 1);
        var later = UtcNow.AddMinutes(5);

        var result = target.Merge(source, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(later, target.UpdatedAtUtc);
    }

    [Fact]
    public void Merge_NoDuplicateVariantId_AfterMerge()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 1);
        AddItem(target, VariantId.Create(), quantity: 1);
        AddItem(source, shared, quantity: 2);
        AddItem(source, VariantId.Create(), quantity: 1);

        var result = target.Merge(source, UtcNow);

        Assert.True(result.IsSuccess);
        var distinctVariantCount = target.Items.Select(item => item.VariantId).Distinct().Count();
        Assert.Equal(target.Items.Count, distinctVariantCount);
    }

    [Fact]
    public void Merge_RepeatWithSameEmptiedSource_IsNoOp()
    {
        var target = CreateAuthenticatedCart(out _);
        var source = CreateGuestCart();
        var shared = VariantId.Create();
        AddItem(target, shared, quantity: 2);
        AddItem(source, shared, quantity: 3);

        var first = target.Merge(source, UtcNow);
        var targetVersion = target.Version;

        var second = target.Merge(source, UtcNow);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(target.Items);
        Assert.Equal(5, Assert.Single(target.Items).Quantity);
        Assert.Equal(targetVersion, target.Version);
    }

    [Fact]
    public void Merge_WithSelf_Throws()
    {
        var cart = CreateGuestCart();
        AddItem(cart, VariantId.Create(), quantity: 1);

        Assert.Throws<ArgumentException>(() => cart.Merge(cart, UtcNow));
    }
}
