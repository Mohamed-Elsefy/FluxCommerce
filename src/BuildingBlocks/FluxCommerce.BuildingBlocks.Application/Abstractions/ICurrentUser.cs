namespace FluxCommerce.BuildingBlocks.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string[] Roles { get; }
    bool IsAuthenticated { get; }
}
