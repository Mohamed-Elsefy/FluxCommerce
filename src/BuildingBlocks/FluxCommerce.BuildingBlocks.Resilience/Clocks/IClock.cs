namespace FluxCommerce.BuildingBlocks.Resilience.Clocks;

public interface IClock
{
    DateTime UtcNow { get; }

    DateTimeOffset UtcNowOffset { get; }
}
