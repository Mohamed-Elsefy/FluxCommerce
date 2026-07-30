namespace FluxCommerce.BuildingBlocks.Persistence.Configurations;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
