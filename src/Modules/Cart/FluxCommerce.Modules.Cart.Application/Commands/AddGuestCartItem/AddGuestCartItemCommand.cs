using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;

/// <summary>
/// Adds an item to a Guest Cart. The item carries the temporary snapshot data (SKU, product
/// name, unit price, currency) supplied by the caller; the Cart is resolved and owned-checked
/// by the handler before the Domain Add behavior runs.
/// </summary>
public sealed record AddGuestCartItemCommand(
    Guid CartId,
    Guid VariantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPriceAmount,
    string Currency,
    int Quantity) : ICommand<Result<CartState>>;
