namespace FluxCommerce.Modules.Orders.Application.Models;

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
