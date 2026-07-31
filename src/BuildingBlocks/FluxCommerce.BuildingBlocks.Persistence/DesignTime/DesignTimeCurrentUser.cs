using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.BuildingBlocks.Persistence.DesignTime;

public sealed class DesignTimeCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => null;
    public string[] Roles => [];
    public bool IsAuthenticated => false;
}
