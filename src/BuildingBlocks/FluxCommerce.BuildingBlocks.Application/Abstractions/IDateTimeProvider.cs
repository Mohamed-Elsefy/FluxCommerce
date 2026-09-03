namespace FluxCommerce.BuildingBlocks.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
