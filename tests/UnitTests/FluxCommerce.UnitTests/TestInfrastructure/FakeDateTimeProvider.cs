using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
}
