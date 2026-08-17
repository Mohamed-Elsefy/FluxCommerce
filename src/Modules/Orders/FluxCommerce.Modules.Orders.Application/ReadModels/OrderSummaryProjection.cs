namespace FluxCommerce.Modules.Orders.Application.ReadModels;

/// <summary>
/// Phase 7.9: the durable denormalized read model maintained by the Order Summary projection consumer.
/// It mirrors the source <c>OrderCreated</c> aggregate at projection time (identity, status, totals,
/// item count) and pins the trace identifiers of the delivery that produced it (Phase 7.8
/// correlation/causation propagation), so a projection row is observable evidence of exactly one
/// idempotent business effect. The record is transport- and persistence-agnostic: primitive-only
/// fields, no Domain value objects, no EF or Infrastructure types.
/// </summary>
public sealed record OrderSummaryProjection(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string Status,
    decimal GrandTotalAmount,
    string GrandTotalCurrency,
    int ItemCount,
    Guid CorrelationId,
    Guid? CausationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ProjectedAtUtc);