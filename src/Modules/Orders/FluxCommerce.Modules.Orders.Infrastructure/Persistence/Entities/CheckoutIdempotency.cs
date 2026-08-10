namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;

public sealed class CheckoutIdempotency
{
    public const int IdempotencyKeyMaxLength = 128;

    public const int RequestFingerprintMaxLength = 512;

    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public Guid OrderId { get; init; }

    public string RequestFingerprint { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public static CheckoutIdempotency Create(
        Guid id,
        Guid userId,
        string idempotencyKey,
        Guid orderId,
        string requestFingerprint,
        DateTimeOffset createdAtUtc) => new()
    {
        Id = id,
        UserId = userId,
        IdempotencyKey = idempotencyKey,
        OrderId = orderId,
        RequestFingerprint = requestFingerprint,
        CreatedAtUtc = createdAtUtc
    };
}
