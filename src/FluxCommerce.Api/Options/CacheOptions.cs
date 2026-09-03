using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Api.Options;

public class CacheOptions
{
    public const string SectionName = "Cache";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    public int DefaultExpirationMinutes { get; init; } = 60;
}
