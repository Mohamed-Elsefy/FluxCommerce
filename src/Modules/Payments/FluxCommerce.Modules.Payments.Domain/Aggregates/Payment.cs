using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Payments.Domain.Errors;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Aggregates;

public sealed class Payment : AggregateRoot<Guid>
{
    private PaymentStatus _status;
    private long _versionBacking;

    public Guid PaymentId => Id;
    public Guid OrderId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string? ProviderPaymentIntentId { get; private set; }
    public string? Provider { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public PaymentStatus Status => _status;

    // Private constructor for EF Core
    private Payment()
        : base(Guid.Empty)
    {
        _status = PaymentStatus.Pending;
    }

    public Payment(Guid paymentId, Guid orderId, decimal amount, string currency)
        : base(paymentId)
    {
        if (paymentId == Guid.Empty) throw new ArgumentException("PaymentId is required.", nameof(paymentId));
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (amount <= 0) throw new ArgumentException(PaymentErrors.InvalidAmount.Description, nameof(amount));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException(PaymentErrors.InvalidCurrency.Description, nameof(currency));

        OrderId = orderId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        _status = PaymentStatus.Pending;
        Version = 1;
        _versionBacking = 1;
        CreatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new PaymentCreatedDomainEvent(Id, OrderId, Amount, Currency));
    }

    public void Process()
    {
        if (_status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled)
            throw new InvalidOperationException(PaymentErrors.InvalidStateTransition.Description);

        if (_status == PaymentStatus.Processing) return;

        _status = PaymentStatus.Processing;
        BumpVersion();
        AddDomainEvent(new PaymentProcessingDomainEvent(Id, OrderId, Amount, Currency));
    }

    public void Succeed(string? providerPaymentIntentId = null)
    {
        if (_status == PaymentStatus.Succeeded)
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadySucceeded.Description);
        if (_status == PaymentStatus.Failed)
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyFailed.Description);
        if (_status == PaymentStatus.Cancelled)
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyCancelled.Description);

        _status = PaymentStatus.Succeeded;
        if (!string.IsNullOrWhiteSpace(providerPaymentIntentId))
            ProviderPaymentIntentId = providerPaymentIntentId;
        BumpVersion();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddDomainEvent(new PaymentSucceededDomainEvent(Id, OrderId, Amount, Currency, ProviderPaymentIntentId, Provider));
    }

    public void Fail(string failureReason = "")
    {
        if (_status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled)
            throw new InvalidOperationException(PaymentErrors.InvalidStateTransition.Description);

        _status = PaymentStatus.Failed;
        BumpVersion();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, Amount, Currency, failureReason));
    }

    public void Cancel()
    {
        if (_status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled)
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyCancelled.Description);

        _status = PaymentStatus.Cancelled;
        BumpVersion();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        AddDomainEvent(new PaymentCancelledDomainEvent(Id, OrderId));
    }

    public void UpdateProviderReference(string providerPaymentIntentId, string provider)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentIntentId))
            throw new ArgumentException(PaymentErrors.ProviderPaymentIntentIdRequired.Description, nameof(providerPaymentIntentId));
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (_status == PaymentStatus.Succeeded)
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadySucceeded.Description);

        ProviderPaymentIntentId = providerPaymentIntentId;
        Provider = provider;
        BumpVersion();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool CanBePaid() => _status is PaymentStatus.Pending or PaymentStatus.Processing;

    private void BumpVersion()
    {
        Version++;
        _versionBacking = Version;
    }
}
