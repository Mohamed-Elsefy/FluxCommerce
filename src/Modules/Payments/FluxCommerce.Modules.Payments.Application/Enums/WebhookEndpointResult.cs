namespace FluxCommerce.Modules.Payments.Application.Enums;

/// <summary>
/// Outcome of handling one Stripe webhook HTTP delivery.
/// The API boundary maps the first three to <c>400 Bad Request</c> and the last
/// two to <c>200 OK</c> (duplicates must stop Stripe retries).
/// </summary>
public enum WebhookEndpointResult
{
    MissingSignature,
    EmptyPayload,
    InvalidSignature,
    Processed,
    AlreadyProcessed
}
