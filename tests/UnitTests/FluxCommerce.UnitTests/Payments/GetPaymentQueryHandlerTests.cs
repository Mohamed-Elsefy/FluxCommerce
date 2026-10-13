using FluxCommerce.Modules.Payments.Application.Queries.GetPayment;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class GetPaymentQueryHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public GetPaymentQueryHandlerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite(_connection).Options;
        var ctx = new PaymentsDbContext(options, new FakeDateTimeProvider(), new FakeCurrentUser(), new FakeSaveChangesInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor { }

    [Fact]
    public async Task Owner_GetsPayment_Success()
    {
        var owner = Guid.NewGuid();
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", owner);
        var ctx = CreateContext();
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        await ctx.SaveChangesAsync();

        var handler = new GetPaymentQueryHandler(
            new FakeCurrentUser { UserId = owner, IsAuthenticated = true },
            new PaymentAdminStore(ctx));

        var result = await handler.HandleAsync(new GetPaymentQuery(payment.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(payment.Id, result.Value!.Id);
        Assert.Equal(owner, result.Value!.CustomerId);
    }

    [Fact]
    public async Task MissingPayment_ReturnsNotFound()
    {
        var ctx = CreateContext();
        var handler = new GetPaymentQueryHandler(
            new FakeCurrentUser { UserId = Guid.NewGuid(), IsAuthenticated = true },
            new PaymentAdminStore(ctx));

        var result = await handler.HandleAsync(new GetPaymentQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task UnauthenticatedCaller_ReturnsNotFound_Masked()
    {
        var owner = Guid.NewGuid();
        var payment = new FluxCommerce.Modules.Payments.Domain.Aggregates.Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", owner);
        var ctx = CreateContext();
        ctx.Set<FluxCommerce.Modules.Payments.Domain.Aggregates.Payment>().Add(payment);
        await ctx.SaveChangesAsync();

        var handler = new GetPaymentQueryHandler(
            new FakeCurrentUser { UserId = null, IsAuthenticated = false },
            new PaymentAdminStore(ctx));

        var result = await handler.HandleAsync(new GetPaymentQuery(payment.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.NotFound", result.Error.Code);
    }
}
