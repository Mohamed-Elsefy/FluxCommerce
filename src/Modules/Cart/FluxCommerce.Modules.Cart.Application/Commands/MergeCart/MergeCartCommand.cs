using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.MergeCart;

/// <summary>
/// Phase 3.6.1: merges a Guest Cart into the current authenticated user's active Cart
/// (Guest â†’ Authenticated). The source Guest Cart is identified only by its opaque identifier;
/// the authenticated target owner is always resolved from <see cref="ICurrentUser"/>. The command
/// never carries a UserId or a CartId for the target from request data.
/// </summary>
public sealed record MergeCartCommand(Guid GuestCartId) : ICommand<Result<CartState>>;

