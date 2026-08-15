using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Api.Options;

public class MessageBrokerOptions
{
    public const string SectionName = "MessageBroker";

    [Required]
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 5672;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string VirtualHost { get; init; } = "/";

    public string ExchangeName { get; init; } = "fluxcommerce.events";

    public int RetryCount { get; init; } = 5;
}
