namespace FluxCommerce.Modules.Orders.Contracts.Responses;

/// <summary>
/// Minimal Checkout outcome returned to the client: the placed Order identifier, its public number,
/// current status and creation timestamp. The full Order representation is deliberately deferred to
/// the Phase 4.9 Orders queries.
/// </summary>
public sealed record CheckoutResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAtUtc);