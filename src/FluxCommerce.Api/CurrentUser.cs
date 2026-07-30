using FluxCommerce.BuildingBlocks.Persistence;

namespace FluxCommerce.Api;

public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => null;
    public string[] Roles => [];
    public bool IsAuthenticated => false;
}
