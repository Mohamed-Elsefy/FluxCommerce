using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;

/// <summary>
/// Adds an item to the current authenticated user's active Cart. The UserId is resolved from
/// <see cref="ICurrentUser"/>; the request carries no Cart or User identifier. The item carries the
/// temporary snapshot data (SKU, product name, unit price, currency) supplied by the caller; the
/// Cart is resolved and ownership-checked by the handler before the Domain Add behavior runs.
/// </summary>
public sealed record AddAuthenticatedCartItemCommand(
    Guid VariantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPriceAmount,
    string Currency,
    int Quantity) : ICommand<Result<CartState>>;
