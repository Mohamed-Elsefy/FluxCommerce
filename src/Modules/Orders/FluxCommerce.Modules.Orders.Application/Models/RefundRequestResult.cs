using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Domain.Aggregates;

namespace FluxCommerce.Modules.Orders.Application.Models;

/// <summary>
/// Result of an administrative refund request, carrying the outcome and the current
/// Order (when it exists) for response mapping.
/// </summary>
/// <param name="Outcome">What the saga decided.</param>
/// <param name="Order">The current Order, or <c>null</c> when it was not found.</param>
public sealed record RefundRequestResult(RefundRequestOutcome Outcome, Order? Order);
