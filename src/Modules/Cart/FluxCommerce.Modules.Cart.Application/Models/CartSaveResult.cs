using FluxCommerce.Modules.Cart.Application.Enums;

namespace FluxCommerce.Modules.Cart.Application.Models;

public sealed record CartSaveResult(CartSaveStatus Status, CartState? Cart);
