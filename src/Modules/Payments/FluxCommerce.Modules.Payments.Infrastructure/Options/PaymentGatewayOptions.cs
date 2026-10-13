using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Payments.Infrastructure.Options;

/// <summary>
/// Payment provider configuration, bound once from the <c>Stripe</c> section and
/// validated at startup. Owned by Infrastructure: it describes how the Stripe
/// adapter authenticates, not payment business rules. Secrets are externalized
/// via configuration/user-secrets and never hardcoded.
/// </summary>
public class PaymentGatewayOptions
{
    public const string SectionName = "Stripe";

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string WebhookSecret { get; init; } = string.Empty;

    public string PublishableKey { get; init; } = string.Empty;
}
