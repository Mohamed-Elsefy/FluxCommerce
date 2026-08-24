using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Payments.Domain.Options;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string WebhookSecret { get; init; } = string.Empty;

    [Required]
    public string PublishableKey { get; init; } = string.Empty;
}