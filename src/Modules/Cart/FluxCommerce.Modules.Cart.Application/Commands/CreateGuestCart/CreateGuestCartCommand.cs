using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;

/// <summary>
/// Creates a new empty Guest Cart. The server generates the opaque Guest Cart identifier.
/// </summary>
public sealed record CreateGuestCartCommand : ICommand<Result<CartState>>;

