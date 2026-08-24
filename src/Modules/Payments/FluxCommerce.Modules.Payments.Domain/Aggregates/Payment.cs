using FluxCommerce.BuildingBlocks.Domain;
using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Payments.Domain.Errors;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Aggregates;

public sealed class Payment : AggregateRoot<Guid>
{
    private PaymentStatus _status;
    private long _version;

    public Guid PaymentId { get; }
    public Guid OrderId { get; private init; }
    public string? Currency { get; private init; }
    public decimal Amount { get; private init; }
    public string? ProviderPaymentIntentId { get; private set; }
    public string? Provider { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public new IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();

    private readonly List<IDomainEvent> _domainEvents = [];

    // Private constructor for EF Core / deserialization
    private Payment()
        : base(default)
    {
        _status = PaymentStatus.Pending;
        _version = 0;
    }

    public Payment(Guid paymentId, Guid orderId, decimal amount, string currency)
        : base(paymentId)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        _status = PaymentStatus.Pending;
        _version = 1;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Process()
    {
        if (_status == PaymentStatus.Succeeded || _status == PaymentStatus.Failed || _status == PaymentStatus.Cancelled)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadySucceeded.Description);
        }

        _status = PaymentStatus.Processing;
        _version++;
        AddDomainEvent(new PaymentProcessingDomainEvent(PaymentId, OrderId, Amount, Currency));
    }

    public void Succeed(string? providerPaymentIntentId = null)
    {
        if (_status == PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadySucceeded.Description);
        }

        if (_status == PaymentStatus.Failed)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyFailed.Description);
        }

        if (_status == PaymentStatus.Cancelled)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyCancelled.Description);
        }

        _status = PaymentStatus.Succeeded;
        _version++;

        ProviderPaymentIntentId = providerPaymentIntentId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new PaymentSucceededDomainEvent(PaymentId, OrderId, Amount, Currency, ProviderPaymentIntentId));
    }

    public void Fail(string failureReason = "")
    {
        if (_status == PaymentStatus.Succeeded || _status == PaymentStatus.Failed || _status == PaymentStatus.Cancelled)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyFailed.Description);
        }

        _status = PaymentStatus.Failed;
        _version++;

        AddDomainEvent(new PaymentFailedDomainEvent(PaymentId, OrderId, Amount, Currency, failureReason));
    }

    public void Cancel()
    {
        if (_status == PaymentStatus.Succeeded || _status == PaymentStatus.Failed || _status == PaymentStatus.Cancelled)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadyCancelled.Description);
        }

        _status = PaymentStatus.Cancelled;
        _version++;

        AddDomainEvent(new PaymentCancelledDomainEvent(PaymentId, OrderId));
    }

    public void UpdateProviderReference(string providerPaymentIntentId, string provider)
    {
        if (_status == PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException(PaymentErrors.PaymentAlreadySucceeded.Description);
        }

        ProviderPaymentIntentId = providerPaymentIntentId;
        Provider = provider;
        _version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public bool CanBePaid() => _status == PaymentStatus.Pending || _status == PaymentStatus.Processing;
}