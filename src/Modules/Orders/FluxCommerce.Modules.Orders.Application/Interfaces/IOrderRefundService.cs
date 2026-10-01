using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence-driven port for the administrative order refund flow. The implementation
/// drives the Orders-owned saga orchestrator (staging <c>payments.refund_payment_requested</c>
/// through the module Outbox) and commits the unit of work. Authorization is enforced at
/// the API boundary (<c>AdminPolicy</c>).
/// </summary>
public interface IOrderRefundService
{
    Task<RefundRequestResult> RequestRefundAsync(
        OrderId orderId,
        string reason,
        CancellationToken cancellationToken = default);
}
