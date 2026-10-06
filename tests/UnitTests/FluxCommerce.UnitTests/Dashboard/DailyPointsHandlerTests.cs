using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentDailyPoints;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Dashboard;

/// <summary>
/// Coverage for the Phase 23 F3 daily-points handlers: trailing-window math anchored on the
/// shared clock, pass-through of the store projection, and the below-one guard.
/// </summary>
public sealed class DailyPointsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OrderHandler_ComputesTrailingWindowFromClock()
    {
        var store = new Mock<IOrderStore>();
        var clock = new FakeDateTimeProvider { UtcNow = Now.UtcDateTime };
        var expected = new[] { new OrderDailyPointReadModel(Now.AddHours(-1)) };
        store.Setup(s => s.GetCreatedPointsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await new GetOrderDailyPointsQueryHandler(store.Object, clock)
            .HandleAsync(new GetOrderDailyPointsQuery(5));

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Value);
        store.Verify(s => s.GetCreatedPointsAsync(Now.AddDays(-5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrderHandler_NonPositiveDays_ReturnsValidationWithoutCallingStore()
    {
        var store = new Mock<IOrderStore>();
        var clock = new FakeDateTimeProvider { UtcNow = Now.UtcDateTime };

        var result = await new GetOrderDailyPointsQueryHandler(store.Object, clock)
            .HandleAsync(new GetOrderDailyPointsQuery(0));

        Assert.True(result.IsFailure);
        store.Verify(s => s.GetCreatedPointsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaymentHandler_ComputesTrailingWindowFromClock()
    {
        var store = new Mock<IPaymentAdminStore>();
        var clock = new FakeDateTimeProvider { UtcNow = Now.UtcDateTime };
        var expected = new[] { new PaymentDailyPointReadModel(Now.AddHours(-1), 10.00m, "USD", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded) };
        store.Setup(s => s.GetDailyPointsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await new GetPaymentDailyPointsQueryHandler(store.Object, clock)
            .HandleAsync(new GetPaymentDailyPointsQuery(7));

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Value);
        store.Verify(s => s.GetDailyPointsAsync(Now.AddDays(-7), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentHandler_NonPositiveDays_ReturnsValidationWithoutCallingStore()
    {
        var store = new Mock<IPaymentAdminStore>();
        var clock = new FakeDateTimeProvider { UtcNow = Now.UtcDateTime };

        var result = await new GetPaymentDailyPointsQueryHandler(store.Object, clock)
            .HandleAsync(new GetPaymentDailyPointsQuery(-3));

        Assert.True(result.IsFailure);
        store.Verify(s => s.GetDailyPointsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
