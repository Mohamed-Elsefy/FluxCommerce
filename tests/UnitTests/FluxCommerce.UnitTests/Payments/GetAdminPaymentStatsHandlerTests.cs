using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class GetAdminPaymentStatsHandlerTests
{
    private readonly Mock<IPaymentAdminStore> _store = new();

    private GetAdminPaymentStatsQueryHandler CreateHandler() => new(_store.Object);

    [Fact]
    public async Task HandleAsync_MapsSucceededAndRefundedTotals()
    {
        _store.Setup(s => s.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAdminStatsReadModel(
                Total: 3,
                Pending: 0,
                Processing: 0,
                Succeeded: 2,
                Failed: 0,
                Cancelled: 0,
                Refunded: 1,
                TotalsByCurrency:
                [
                    new PaymentCurrencyTotalReadModel("USD", 2, 100.00m),
                ],
                RefundedTotalsByCurrency:
                [
                    new PaymentCurrencyTotalReadModel("USD", 1, 40.00m),
                ]));

        var result = await CreateHandler().HandleAsync(new GetAdminPaymentStatsQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Total);
        Assert.Single(result.Value.TotalsByCurrency);
        Assert.Equal("USD", result.Value.TotalsByCurrency[0].Currency);
        Assert.Equal(2, result.Value.TotalsByCurrency[0].SucceededCount);
        Assert.Equal(100.00m, result.Value.TotalsByCurrency[0].SucceededTotalAmount);
        Assert.Single(result.Value.RefundedTotalsByCurrency);
        Assert.Equal("USD", result.Value.RefundedTotalsByCurrency[0].Currency);
        Assert.Equal(1, result.Value.RefundedTotalsByCurrency[0].SucceededCount);
        Assert.Equal(40.00m, result.Value.RefundedTotalsByCurrency[0].SucceededTotalAmount);
    }

    [Fact]
    public async Task HandleAsync_EmptyRefundedTotals_MapsEmptyList()
    {
        _store.Setup(s => s.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentAdminStatsReadModel(0, 0, 0, 0, 0, 0, 0, [], []));

        var result = await CreateHandler().HandleAsync(new GetAdminPaymentStatsQuery());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.TotalsByCurrency);
        Assert.Empty(result.Value.RefundedTotalsByCurrency);
    }
}
