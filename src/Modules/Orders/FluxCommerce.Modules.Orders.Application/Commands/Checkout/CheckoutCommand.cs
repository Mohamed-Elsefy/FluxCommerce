using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Domain.Aggregates;

namespace FluxCommerce.Modules.Orders.Application.Commands.Checkout;

/// <summary>
/// Commands the Orders module to place an irreversible Checkout Order snapshot for the authenticated
/// customer's Cart. The <c>IdempotencyKey</c> is a client-supplied identifier read from the
/// <c>Idempotency-Key</c> request header; it participates in the unique (Customer, Key) constraint so
/// a repeated checkout with the same key and request never creates a second order.
/// </summary>
public sealed record CheckoutCommand(
    Guid CartId,
    string IdempotencyKey,
    CheckoutAddressData ShippingAddress,
    CheckoutAddressData BillingAddress) : ICommand<Result<Order>>;