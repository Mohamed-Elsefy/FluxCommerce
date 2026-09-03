using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;

/// <summary>
/// Adds an item to the current authenticated user's active Cart. The UserId is resolved from
/// <see cref="ICurrentUser"/>; the request carries no Cart or User identifier. The caller supplies
/// only the Variant identifier, the temporary unit price snapshot (amount + currency) and the
/// requested quantity; the Cart-owned snapshot data (product identity, SKU, product name) is
/// resolved from the Catalog contract by the handler before the Domain Add behavior runs.
/// </summary>
public sealed record AddAuthenticatedCartItemCommand(
    Guid VariantId,
    decimal UnitPriceAmount,
    string Currency,
    int Quantity) : ICommand<Result<CartState>>;
