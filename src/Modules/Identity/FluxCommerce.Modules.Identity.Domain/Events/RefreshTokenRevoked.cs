using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Identity.Domain.Events;

public sealed class RefreshTokenRevoked : DomainEvent
{
    public Guid UserId { get; }

    public Guid TokenId { get; }

    public RefreshTokenRevoked(Guid userId, Guid tokenId)
    {
        UserId = userId;
        TokenId = tokenId;
    }
}
