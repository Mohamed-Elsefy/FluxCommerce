using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Orders.Application.Errors;

/// <summary>
/// Application-level errors raised by the customer-facing Orders queries and lifecycle commands
/// (pagination validation, ownership-scoped reads and cancellations).
/// </summary>
public static class OrderQueryErrors
{
    public static readonly Error AuthenticationRequired = Error.Unauthorized(
        "Order.QueryAuthenticationRequired",
        "Authentication is required to access this resource.");

    public static readonly Error ValidationFailed = Error.Validation(
        "Order.ValidationFailed",
        "The order query parameters are invalid.");

    public static readonly Error NotFound = Error.NotFound(
        "Order.NotFound",
        "The order was not found.");

    public static readonly Error CancelConflict = Error.Conflict(
        "Order.CancelConflict",
        "The order cannot be cancelled in its current state.");

    public static readonly Error RefundConflict = Error.Conflict(
        "Order.RefundConflict",
        "The order cannot be refunded in its current state.");
}
