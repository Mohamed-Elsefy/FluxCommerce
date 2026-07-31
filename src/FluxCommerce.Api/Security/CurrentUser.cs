using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.Api.Security;

public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => null;
    public string[] Roles => [];
    public bool IsAuthenticated => false;
}
