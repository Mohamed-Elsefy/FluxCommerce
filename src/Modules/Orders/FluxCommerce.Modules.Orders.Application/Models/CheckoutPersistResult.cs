using FluxCommerce.Modules.Orders.Application.Enums;

namespace FluxCommerce.Modules.Orders.Application.Models;

public sealed record CheckoutPersistResult(
    CheckoutPersistStatus Status,
    CheckoutIdempotencyRecord? Existing);
