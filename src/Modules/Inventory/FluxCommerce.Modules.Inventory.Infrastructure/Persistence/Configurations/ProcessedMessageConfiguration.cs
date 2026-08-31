using FluxCommerce.BuildingBlocks.Idempotency.Configuration;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>
/// Module-specific idempotency mapping. Inherits shared mapping to keep schema consistent
/// while retaining isolated <c>ProcessedMessages</c> table per DbContext.
/// </summary>
public sealed class ProcessedMessageConfiguration : ProcessedMessageConfigurationBase
{
}