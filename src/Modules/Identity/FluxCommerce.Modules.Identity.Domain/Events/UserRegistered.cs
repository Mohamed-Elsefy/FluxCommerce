using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Identity.Domain.Events;

public sealed class UserRegistered : DomainEvent
{
    public Guid UserId { get; }

    public string Email { get; }

    public UserRegistered(Guid userId, string email)
    {
        UserId = userId;
        Email = email;
    }
}
