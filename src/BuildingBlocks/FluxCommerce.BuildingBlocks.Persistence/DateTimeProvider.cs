using FluxCommerce.BuildingBlocks.Persistence.Configurations;

namespace FluxCommerce.BuildingBlocks.Persistence;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
