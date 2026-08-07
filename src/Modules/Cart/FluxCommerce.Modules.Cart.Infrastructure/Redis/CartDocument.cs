namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed record CartDocument(
    Guid CartId,
    Guid? UserId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<CartItemDocument> Items);
