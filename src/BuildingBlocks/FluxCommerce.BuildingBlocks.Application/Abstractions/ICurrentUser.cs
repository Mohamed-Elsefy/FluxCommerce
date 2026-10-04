namespace FluxCommerce.BuildingBlocks.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string[] Roles { get; }

    /// <summary>
    /// Action-level permission values granted to the caller (Phase 22). Sourced from the access
    /// token's permission claims on the request path; empty for background, design-time and test
    /// doubles that carry no request identity.
    /// </summary>
    IReadOnlyCollection<string> Permissions { get; }

    bool IsAuthenticated { get; }
}
