using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Orders.Contracts.Requests;

/// <summary>
/// Administrative request to refund a paid Order through the saga compensation flow.
/// The refund itself is processed asynchronously; the response carries the current Order.
/// </summary>
public sealed record RefundOrderRequest(
    [Required][StringLength(500, MinimumLength = 1)] string Reason);
