using FluxCommerce.BuildingBlocks.Outbox.Configuration;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Module-specific outbox mapping. Inherits shared mapping to keep schema consistent
/// while retaining isolated table per DbContext.
/// </summary>
public sealed class OutboxMessageConfiguration : OutboxMessageConfigurationBase
{
}
