using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Services;

public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
