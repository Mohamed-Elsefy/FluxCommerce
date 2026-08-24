namespace FluxCommerce.Modules.Payments.Domain.Errors;

public sealed record DomainError
{
    public string Code { get; }

    public string Description { get; }

    private DomainError(string code, string description, bool isSentinel = false)
    {
        if (!isSentinel)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("An error code is required.", nameof(code));

            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("An error description is required.", nameof(description));
        }

        Code = code;
        Description = description;
    }

    public static DomainError Create(string code, string description) => new(code, description);

    public static readonly DomainError None = new(string.Empty, string.Empty, isSentinel: true);
}

public static class PaymentErrors
{
    public static readonly DomainError InvalidAmount = DomainError.Create(
        "Payment.InvalidAmount",
        "Payment amount must be greater than zero.");

    public static readonly DomainError InvalidCurrency = DomainError.Create(
        "Payment.InvalidCurrency",
        "Payment currency must be a valid ISO 4217 currency code.");

    public static readonly DomainError InvalidStateTransition = DomainError.Create(
        "Payment.InvalidStateTransition",
        "Payment cannot transition to the requested state from its current state.");

    public static readonly DomainError PaymentAlreadySucceeded = DomainError.Create(
        "Payment.AlreadySucceeded",
        "Payment has already succeeded and cannot be modified.");

    public static readonly DomainError PaymentAlreadyFailed = DomainError.Create(
        "Payment.AlreadyFailed",
        "Payment has already failed and cannot be modified.");

    public static readonly DomainError PaymentAlreadyCancelled = DomainError.Create(
        "Payment.AlreadyCancelled",
        "Payment has already been cancelled and cannot be modified.");

    public static readonly DomainError ProviderPaymentIntentIdRequired = DomainError.Create(
        "Payment.ProviderPaymentIntentIdRequired",
        "Provider Payment Intent ID is required.");

    public static readonly DomainError CurrencyMismatch = DomainError.Create(
        "Payment.CurrencyMismatch",
        "All payment amounts must share the same currency.");
}