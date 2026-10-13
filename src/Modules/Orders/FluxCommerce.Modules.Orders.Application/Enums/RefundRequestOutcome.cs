namespace FluxCommerce.Modules.Orders.Application.Enums;

/// <summary>
/// Outcome of an administrative refund request for an Order.
/// </summary>
public enum RefundRequestOutcome
{
    /// <summary>The saga accepted the refund and staged the refund request.</summary>
    Applied = 0,

    /// <summary>A refund is already in flight or completed; no second request was staged.</summary>
    Duplicate = 1,

    /// <summary>The order is not eligible for a refund in its current state.</summary>
    NotEligible = 2,

    /// <summary>No such order exists.</summary>
    OrderNotFound = 3,
}
