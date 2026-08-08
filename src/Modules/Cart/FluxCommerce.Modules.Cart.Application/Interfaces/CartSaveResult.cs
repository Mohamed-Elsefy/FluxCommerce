namespace FluxCommerce.Modules.Cart.Application.Interfaces;

public sealed record CartSaveResult(CartSaveStatus Status, CartState? Cart);
