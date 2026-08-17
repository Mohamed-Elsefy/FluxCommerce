namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Phase 7.9 persistence row for the Order Summary projection read model. Primitive-only columns that
/// mirror the source <c>OrderCreated</c> aggregate at projection time plus the Phase 7.8 trace
/// identifiers of the delivery that produced it. The row is staged by the projection store and
/// committed atomically with the processed-message record by the idempotent-processing boundary.
/// </summary>
public sealed class OrderSummaryProjectionEntity
{
    public const int OrderNumberMaxLength = 32;

    public const int StatusMaxLength = 16;

    public const int GrandTotalCurrencyMaxLength = 3;

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string Status { get; init; } = string.Empty;

    public decimal GrandTotalAmount { get; init; }

    public string GrandTotalCurrency { get; init; } = string.Empty;

    public int ItemCount { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ProjectedAtUtc { get; init; }

    public static OrderSummaryProjectionEntity Create(
        Guid orderId,
        string orderNumber,
        Guid customerId,
        string status,
        decimal grandTotalAmount,
        string grandTotalCurrency,
        int itemCount,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset projectedAtUtc) => new()
    {
        OrderId = orderId,
        OrderNumber = orderNumber,
        CustomerId = customerId,
        Status = status,
        GrandTotalAmount = grandTotalAmount,
        GrandTotalCurrency = grandTotalCurrency,
        ItemCount = itemCount,
        CorrelationId = correlationId,
        CausationId = causationId,
        CreatedAtUtc = createdAtUtc,
        ProjectedAtUtc = projectedAtUtc
    };
}