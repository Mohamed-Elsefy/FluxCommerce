using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;

/// <summary>
/// Adds an item to a Guest Cart. The caller supplies only the Variant identifier, the temporary
/// unit price snapshot (amount + currency) and the requested quantity; the Cart-owned snapshot data
/// (product identity, SKU, product name) is resolved from the Catalog contract by the handler before
/// the Domain Add behavior runs.
/// </summary>
public sealed record AddGuestCartItemCommand(
    Guid CartId,
    Guid VariantId,
    decimal UnitPriceAmount,
    string Currency,
    int Quantity) : ICommand<Result<CartState>>;