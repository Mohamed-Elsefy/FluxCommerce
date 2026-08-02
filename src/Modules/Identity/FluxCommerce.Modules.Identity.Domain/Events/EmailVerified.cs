using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Identity.Domain.Events;

public sealed class EmailVerified : DomainEvent
{
    public Guid UserId { get; }

    public EmailVerified(Guid userId)
    {
        UserId = userId;
    }
}
