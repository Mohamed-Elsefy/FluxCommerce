namespace FluxCommerce.Modules.Identity.Contracts.Responses;

public sealed record LoginResponse(
    Guid UserId,
    string Email,
    IReadOnlyCollection<string> Roles,
    string AccessToken,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
