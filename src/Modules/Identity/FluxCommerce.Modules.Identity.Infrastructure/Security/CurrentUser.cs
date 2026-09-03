using System.Security.Claims;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace FluxCommerce.Modules.Identity.Infrastructure.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId =>
        Guid.TryParse(FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    public string? Email => FindFirstValue(ClaimTypes.Email);

    public string[] Roles =>
        _httpContextAccessor.HttpContext?.User.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToArray() ?? [];

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    private string? FindFirstValue(string claimType) =>
        _httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;
}
