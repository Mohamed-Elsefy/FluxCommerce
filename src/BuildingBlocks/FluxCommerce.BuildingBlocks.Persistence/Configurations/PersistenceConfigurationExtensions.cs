using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.BuildingBlocks.Persistence.Configurations;

public static class PersistenceConfigurationExtensions
{
    public static ModelConfigurationBuilder AddPersistenceConventions(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 6);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2");

        return configurationBuilder;
    }
}
