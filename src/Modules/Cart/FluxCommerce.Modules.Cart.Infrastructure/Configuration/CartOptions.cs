using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Cart.Infrastructure.Configuration;

public sealed class CartOptions
{
    public const string SectionName = "Cart";

    [Required]
    [RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "KeyNamespace must contain only letters, digits, underscores, or hyphens.")]
    public string KeyNamespace { get; init; } = "cart";

    [Range(1, 525600, ErrorMessage = "DefaultTtlMinutes must be between 1 and 525600.")]
    public int DefaultTtlMinutes { get; init; } = 7 * 24 * 60;
}
