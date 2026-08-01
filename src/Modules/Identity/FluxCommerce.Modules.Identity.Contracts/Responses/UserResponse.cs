namespace FluxCommerce.Modules.Identity.Contracts.Responses;

public sealed record UserResponse(
    Guid Id,
    string Email,
    IReadOnlyCollection<string> Roles,
    bool IsEmailVerified,
    DateTime RegisteredAtUtc);
