namespace FluxCommerce.BuildingBlocks.Persistence;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string[] Roles { get; }
    bool IsAuthenticated { get; }
}
