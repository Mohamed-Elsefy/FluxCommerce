using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.BuildingBlocks.Persistence.DesignTime;

public sealed class DesignTimeCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => null;
    public string[] Roles => [];

    // BuildingBlocks must not reference Identity.Domain for the claim-type constant;
    // design-time scopes carry no permissions by design.
    public IReadOnlyCollection<string> Permissions => [];
    public bool IsAuthenticated => false;
}
