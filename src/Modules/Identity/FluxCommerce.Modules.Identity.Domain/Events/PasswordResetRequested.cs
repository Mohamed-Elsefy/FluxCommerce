using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Identity.Domain.Events;

public sealed class PasswordResetRequested : DomainEvent
{
    public Guid UserId { get; }

    public PasswordResetRequested(Guid userId)
    {
        UserId = userId;
    }
}
