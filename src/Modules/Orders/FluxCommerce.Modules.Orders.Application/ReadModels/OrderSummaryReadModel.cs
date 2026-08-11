using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.ReadModels;

/// <summary>
/// Read-side projection of an Order for list endpoints. The GrandTotal is computed in the query so a
/// page never materializes the owned line-item collection on the Orders side.
/// </summary>
public sealed record OrderSummaryReadModel(
    OrderId OrderId,
    OrderNumber OrderNumber,
    OrderStatus Status,
    decimal GrandTotalAmount,
    string GrandTotalCurrency,
    int ItemCount,
    DateTimeOffset CreatedAtUtc);
