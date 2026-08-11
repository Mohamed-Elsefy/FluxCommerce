using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence driven port for the Orders Application. The Checkout handler persists the Order
/// snapshot together with its idempotency record in a single atomic unit so a checkout either fully
/// succeeds (Order + (Customer, Key) idempotency row) or fails as a whole. The port also exposes the
/// read-only queries needed to resolve a previously completed checkout.
/// </summary>
public interface ICheckoutStore
{
    Task<CheckoutIdempotencyRecord?> FindIdempotencyRecordAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Order?> GetOrderAsync(OrderId orderId, CancellationToken cancellationToken = default);

    Task<CheckoutPersistResult> PersistAsync(
        Order order,
        CheckoutIdempotencyRecord idempotency,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-level projection of the (Customer, Idempotency-Key) checkout record. The persistence
/// implementation maps to/from its internal entity model.
/// </summary>
public sealed record CheckoutIdempotencyRecord(
    Guid Id,
    Guid UserId,
    string IdempotencyKey,
    Guid OrderId,
    string RequestFingerprint,
    DateTimeOffset CreatedAtUtc);

public enum CheckoutPersistStatus
{
    Created,
    Duplicate
}

public sealed record CheckoutPersistResult(
    CheckoutPersistStatus Status,
    CheckoutIdempotencyRecord? Existing);