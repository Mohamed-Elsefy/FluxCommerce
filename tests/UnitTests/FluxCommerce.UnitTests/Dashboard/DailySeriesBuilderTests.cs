using FluxCommerce.Api.Models;
using Xunit;

namespace FluxCommerce.UnitTests.Dashboard;

/// <summary>
/// Coverage for the Phase 23 F3 in-memory day bucketing: trailing window, zero-filled gaps,
/// succeeded-only multi-currency revenue, stable date labels, oldest-first ordering.
/// </summary>
public sealed class DailySeriesBuilderTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_EmptyInputs_ReturnsZeroFilledWindowOldestFirst()
    {
        var points = DailySeriesBuilder.Build([], [], 3, Today);

        Assert.Equal(["2026-10-03", "2026-10-04", "2026-10-05"], points.Select(point => point.Date).ToList());
        Assert.All(points, point =>
        {
            Assert.Equal(0, point.Orders);
            Assert.Empty(point.Revenue);
        });
    }

    [Fact]
    public void Build_BucketsOrdersAndSucceededRevenuePerDay()
    {
        var orders = new[]
        {
            Today.AddDays(-2).AddHours(1),
            Today.AddDays(-2).AddHours(5),
            Today.AddHours(-1),
        };
        var payments = new DailySeriesBuilder.PaymentRevenueInput[]
        {
            new(Today.AddDays(-2), 100.00m, "USD", IsSucceeded: true),
            new(Today.AddDays(-2), 40.00m, "USD", IsSucceeded: true),
            new(Today.AddDays(-1), 50.00m, "EUR", IsSucceeded: true),
            new(Today.AddDays(-1), 999.00m, "USD", IsSucceeded: false),
        };

        var points = DailySeriesBuilder.Build(orders, payments, 3, Today);

        Assert.Equal(3, points.Count);

        var twoDaysAgo = points[0];
        Assert.Equal("2026-10-03", twoDaysAgo.Date);
        Assert.Equal(2, twoDaysAgo.Orders);
        var revenue = Assert.Single(twoDaysAgo.Revenue);
        Assert.Equal("USD", revenue.Currency);
        Assert.Equal(140.00m, revenue.Amount);

        var yesterday = points[1];
        Assert.Equal("2026-10-04", yesterday.Date);
        Assert.Equal(0, yesterday.Orders);
        revenue = Assert.Single(yesterday.Revenue);
        Assert.Equal("EUR", revenue.Currency);
        Assert.Equal(50.00m, revenue.Amount);

        var today = points[2];
        Assert.Equal("2026-10-05", today.Date);
        Assert.Equal(1, today.Orders);
        Assert.Empty(today.Revenue);
    }

    [Fact]
    public void Build_OrdersRevenueByCurrencyAcrossCurrencies()
    {
        var payments = new DailySeriesBuilder.PaymentRevenueInput[]
        {
            new(Today, 10.00m, "USD", IsSucceeded: true),
            new(Today, 20.00m, "EUR", IsSucceeded: true),
            new(Today, 5.00m, "USD", IsSucceeded: true),
        };

        var points = DailySeriesBuilder.Build([], payments, 1, Today);

        var point = Assert.Single(points);
        Assert.Equal(2, point.Revenue.Count);
        Assert.Equal("EUR", point.Revenue[0].Currency);
        Assert.Equal(20.00m, point.Revenue[0].Amount);
        Assert.Equal("USD", point.Revenue[1].Currency);
        Assert.Equal(15.00m, point.Revenue[1].Amount);
    }

    [Fact]
    public void Build_PointsOutsideWindow_AreIgnored()
    {
        var orders = new[] { Today.AddDays(-10) };
        var payments = new DailySeriesBuilder.PaymentRevenueInput[]
        {
            new(Today.AddDays(-10), 77.00m, "USD", IsSucceeded: true),
        };

        var points = DailySeriesBuilder.Build(orders, payments, 3, Today);

        Assert.All(points, point =>
        {
            Assert.Equal(0, point.Orders);
            Assert.Empty(point.Revenue);
        });
    }

    [Fact]
    public void Build_NonPositiveDays_ReturnsEmpty()
    {
        Assert.Empty(DailySeriesBuilder.Build([Today], [], 0, Today));
    }
}
