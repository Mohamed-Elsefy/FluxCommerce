using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Identity.Domain.Events;

public sealed class PasswordChanged : DomainEvent
{
    public Guid UserId { get; }

    public PasswordChanged(Guid userId)
    {
        UserId = userId;
    }
}
