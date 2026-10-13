using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Payments.Application.Validation;

/// <summary>
/// Application-level errors raised by the administrative Payments queries.
/// </summary>
public static class PaymentAdminErrors
{
    public static readonly Error ValidationFailed = Error.Validation(
        "Payment.ValidationFailed",
        "The payment query parameters are invalid.");

    public static readonly Error NotFound = Error.NotFound(
        "Payment.NotFound",
        "Payment not found.");
}
