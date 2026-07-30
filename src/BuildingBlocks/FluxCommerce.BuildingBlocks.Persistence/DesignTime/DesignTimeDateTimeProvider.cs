using FluxCommerce.BuildingBlocks.Persistence.Configurations;

namespace FluxCommerce.BuildingBlocks.Persistence.DesignTime;

public sealed class DesignTimeDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
