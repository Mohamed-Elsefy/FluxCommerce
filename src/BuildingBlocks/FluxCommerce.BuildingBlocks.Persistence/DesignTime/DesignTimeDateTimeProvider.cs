using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.BuildingBlocks.Persistence.DesignTime;

public sealed class DesignTimeDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
