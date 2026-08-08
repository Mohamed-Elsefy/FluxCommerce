using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
{
    public static readonly DateTime DefaultUtcNow = new(2026, 8, 7, 10, 30, 0, DateTimeKind.Utc);

    public DateTime UtcNow { get; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public static FixedDateTimeProvider CreateDefault() => new(DefaultUtcNow);
}
