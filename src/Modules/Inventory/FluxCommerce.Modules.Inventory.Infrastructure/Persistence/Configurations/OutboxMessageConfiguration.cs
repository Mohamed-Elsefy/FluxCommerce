using FluxCommerce.BuildingBlocks.Outbox.Configuration;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>
/// Module-specific outbox mapping. Inherits the shared column mapping from
/// <see cref="OutboxMessageConfigurationBase"/> to keep the schema consistent while
/// retaining an isolated <c>OutboxMessages</c> table per DbContext (transactional boundary).
/// </summary>
public sealed class OutboxMessageConfiguration : OutboxMessageConfigurationBase
{
}
