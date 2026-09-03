using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.ValueObjects;

public sealed class CartOwner : ValueObject
{
    public Guid? UserId { get; }

    public bool IsGuest => UserId is null;

    public bool IsAuthenticated => UserId is not null;

    private CartOwner(Guid? userId)
    {
        UserId = userId;
    }

    public static CartOwner Guest() => new(null);

    public static CartOwner Authenticated(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("An authenticated cart owner requires a user id.", nameof(userId));

        return new CartOwner(userId);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return UserId ?? Guid.Empty;
    }
}
