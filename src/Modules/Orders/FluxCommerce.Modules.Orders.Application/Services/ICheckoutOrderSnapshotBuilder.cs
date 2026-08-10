using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Builds the immutable commercial Order snapshot for Checkout using the current (revalidated) Catalog
/// prices. The temporary Cart price snapshot is never treated as authoritative: pricing is always
/// resolved from the current Catalog data returned by the Orders Checkout revalidation contract.
/// </summary>
public interface ICheckoutOrderSnapshotBuilder
{
    Result<Order> Build(
        CartCheckoutSnapshot cart,
        IReadOnlyCollection<CatalogVariantCheckoutInfo> revalidation,
        CustomerId customerId,
        OrderNumber orderNumber,
        OrderAddress shippingAddress,
        OrderAddress billingAddress,
        DateTimeOffset utcNow);
}