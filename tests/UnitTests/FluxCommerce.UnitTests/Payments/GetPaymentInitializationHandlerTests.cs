using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class GetPaymentInitializationHandlerTests
{
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IStripePaymentGateway> _gateway = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GetPaymentInitializationQueryHandler CreateHandler() =>
        new(_repository.Object, _gateway.Object, _currentUser.Object);

    private static Payment CreatePayment(Guid orderId, Guid customerId, string? intentId = "pi_test_123")
    {
        var payment = new Payment(Guid.NewGuid(), orderId, 42.50m, "USD", customerId);
        if (intentId is not null)
            payment.UpdateProviderReference(intentId, "Stripe");
        return payment;
    }

    private void AuthenticateAs(Guid customerId) =>
        _currentUser.SetupGet(u => u.UserId).Returns(customerId);

    private void SetupPayment(Payment? payment) =>
        _repository.Setup(r => r.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

    [Fact]
    public async Task HandleAsync_OwnerWithIntent_ReturnsLiveSecretFromGateway()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var payment = CreatePayment(orderId, customerId);
        AuthenticateAs(customerId);
        SetupPayment(payment);
        _gateway.Setup(g => g.RetrieveClientSecretAsync("pi_test_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("cs_live_secret");

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal(payment.Id, result.Value!.PaymentId);
        Assert.Equal(orderId, result.Value.OrderId);
        Assert.Equal("cs_live_secret", result.Value.ClientSecret);
        Assert.Equal("pi_test_123", result.Value.PaymentReference);
        _gateway.Verify(g => g.RetrieveClientSecretAsync("pi_test_123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_EmptyOrderId_ReturnsValidation()
    {
        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Payment.OrderIdRequired", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsUnauthorized()
    {
        _currentUser.SetupGet(u => u.UserId).Returns((Guid?)null);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Payment.Unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_MissingPayment_ReturnsMaskedNotFound()
    {
        AuthenticateAs(Guid.NewGuid());
        SetupPayment(null);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Payment.NotFound", result.Error.Code);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_StrangerPayment_ReturnsMaskedNotFound()
    {
        var orderId = Guid.NewGuid();
        var payment = CreatePayment(orderId, Guid.NewGuid());
        AuthenticateAs(Guid.NewGuid());
        SetupPayment(payment);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Payment.NotFound", result.Error.Code);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_PaymentWithoutIntent_ReturnsNotReady()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var payment = CreatePayment(orderId, customerId, intentId: null);
        AuthenticateAs(customerId);
        SetupPayment(payment);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Payment.NotReady", result.Error.Code);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_TerminalPayment_ReturnsNotPayable()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var payment = CreatePayment(orderId, customerId);
        payment.Succeed();
        AuthenticateAs(customerId);
        SetupPayment(payment);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Payment.NotPayable", result.Error.Code);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_GatewayReturnsNull_ReturnsProviderUnavailable()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        AuthenticateAs(customerId);
        SetupPayment(CreatePayment(orderId, customerId));
        _gateway.Setup(g => g.RetrieveClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("Payment.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_GatewayThrows_ReturnsProviderUnavailable()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        AuthenticateAs(customerId);
        SetupPayment(CreatePayment(orderId, customerId));
        _gateway.Setup(g => g.RetrieveClientSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe is down"));

        var result = await CreateHandler().HandleAsync(new GetPaymentInitializationQuery(orderId));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal("Payment.ProviderUnavailable", result.Error.Code);
    }
}
