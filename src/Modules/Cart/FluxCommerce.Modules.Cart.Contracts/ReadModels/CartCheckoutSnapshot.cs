namespace FluxCommerce.Modules.Cart.Contracts.ReadModels;

/// <summary>
/// Checkout-specific projection of a Cart. Exposes only the data required by Orders at Checkout:
/// the Cart id, the owning customer, and the Cart items with their stored (temporary) price snapshot.
/// The Cart aggregate, Redis models and Redis store remain internal to the Cart module.
/// </summary>
public sealed record CartCheckoutSnapshot(
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CartCheckoutItemSnapshot> Items);

/// <summary>
/// Checkout-specific Cart line. The price snapshot is the price the Cart stored at add/update time
/// and is never authoritative for Checkout; Orders revalidates current pricing separately.
/// </summary>
public sealed record CartCheckoutItemSnapshot(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    int Quantity,
    decimal UnitPriceAmount,
    string Currency);