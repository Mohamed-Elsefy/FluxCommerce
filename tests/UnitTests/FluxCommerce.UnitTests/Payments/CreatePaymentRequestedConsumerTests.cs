using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Infrastructure.Consuming;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class CreatePaymentRequestedConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public CreatePaymentRequestedConsumerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite(_connection).Options;
        var ctx = new PaymentsDbContext(options, new FakeDateTimeProvider(), new FakeCurrentUser(), new FakeSaveChangesInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private sealed class FakeSaveChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor { }

    private static IntegrationEventEnvelope EnvelopeFrom(CreatePaymentRequestedIntegrationEvent @event) =>
        IntegrationEventEnvelope.From(@event, CreatePaymentRequestedIntegrationEvent.EventType);

    [Fact]
    public async Task HandleAsync_CreatesPayment_AndCallsStripeOnce()
    {
        await using var ctx = CreateContext();
        var gateway = new Mock<IStripePaymentGateway>();
        gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreatePaymentIntentRequest r, CancellationToken _) => new PaymentIntentResult($"pi_{r.OrderId.ToString()[..8]}", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, "cs_test"));

        var consumer = new CreatePaymentRequestedConsumer(
            new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx),
            gateway.Object,
            NullLogger<CreatePaymentRequestedConsumer>.Instance);

        var orderId = Guid.NewGuid();
        var @event = new CreatePaymentRequestedIntegrationEvent(orderId, Guid.NewGuid(), 42.50m, "USD")
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await ctx.SaveChangesAsync();

        var payment = await ctx.Payments.SingleAsync(p => p.OrderId == orderId);
        Assert.Equal(@event.Amount, payment.Amount);
        Assert.Equal(@event.Currency, payment.Currency);
        Assert.NotNull(payment.ProviderPaymentIntentId);
        gateway.Verify(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DuplicateDoesNotCreateSecondPaymentNorSecondStripeCall()
    {
        await using var ctx = CreateContext();
        var callCount = 0;
        var gateway = new Mock<IStripePaymentGateway>();
        gateway.Setup(g => g.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { callCount++; return new PaymentIntentResult("pi_dup_123", FluxCommerce.Modules.Payments.Domain.Enums.PaymentStatus.Succeeded, "cs_dup"); });

        var repo = new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx);
        var consumer = new CreatePaymentRequestedConsumer(repo, gateway.Object, NullLogger<CreatePaymentRequestedConsumer>.Instance);

        var orderId = Guid.NewGuid();
        var @event = new CreatePaymentRequestedIntegrationEvent(orderId, Guid.NewGuid(), 10m, "USD")
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var envelope = EnvelopeFrom(@event);

        await consumer.HandleAsync(@event, envelope);
        await ctx.SaveChangesAsync();

        // Duplicate delivery with same OrderId but different EventId (redelivery)
        var duplicate = @event with { EventId = Guid.NewGuid() };
        var dupEnvelope = EnvelopeFrom(duplicate);
        await consumer.HandleAsync(duplicate, dupEnvelope);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, callCount);
        Assert.Equal(1, await ctx.Payments.CountAsync(p => p.OrderId == orderId));
        var payment = await ctx.Payments.SingleAsync(p => p.OrderId == orderId);
        Assert.Equal("pi_dup_123", payment.ProviderPaymentIntentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task HandleAsync_InvalidEvent_Throws(string? currency)
    {
        await using var ctx = CreateContext();
        var gateway = Mock.Of<IStripePaymentGateway>();
        var consumer = new CreatePaymentRequestedConsumer(
            new FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories.PaymentRepository(ctx),
            gateway,
            NullLogger<CreatePaymentRequestedConsumer>.Instance);

        var @event = new CreatePaymentRequestedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), 10m, currency ?? string.Empty);
        var envelope = EnvelopeFrom(@event);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, envelope));
    }

    public void Dispose() => _connection.Dispose();
}
