using FluxCommerce.BuildingBlocks.Idempotency.Configuration;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Module-specific idempotency mapping. Inherits shared mapping to keep schema consistent
/// while retaining isolated table per DbContext.
/// </summary>
public sealed class ProcessedMessageConfiguration : ProcessedMessageConfigurationBase
{
}
