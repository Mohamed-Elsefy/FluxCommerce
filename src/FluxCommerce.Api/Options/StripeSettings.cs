using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Api.Options;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string WebhookSecret { get; init; } = string.Empty;
}