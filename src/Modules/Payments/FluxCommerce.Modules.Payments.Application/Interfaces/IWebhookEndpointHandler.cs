using FluxCommerce.Modules.Payments.Application.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

/// <summary>
/// HTTP-delivery boundary for provider webhooks: verifies the signature, applies the
/// event idempotently, and reports a controller-mappable outcome. Keeps raw-body
/// handling and verification branching out of the API controller.
/// </summary>
public interface IWebhookEndpointHandler
{
    Task<WebhookEndpointResult> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken cancellationToken = default);
}
