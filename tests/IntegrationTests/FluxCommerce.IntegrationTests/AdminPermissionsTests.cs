using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the SuperAdmin-only permission management surface: grant/revoke/list/catalog plus
/// the refresh-boundary rule (grants reach the access token on the next login).
/// </summary>
[Collection(IdentityTestCollection.Name)]
public class AdminPermissionsTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminPermissionsTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GrantPermission_GrantsAndDuplicateConflicts()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var granted = (await grant.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Contains(PermissionCatalog.OrdersRead, granted.Permissions);

        var duplicate = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task GrantPermission_InvalidInput_ReturnsBadRequest()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var unknown = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = "unknown.permission" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var blank = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task GrantPermission_MissingUser_ReturnsNotFound()
    {
        AuthorizeAsSuperAdmin();

        var response = await _client.PostAsJsonAsync(
            $"admin/users/{Guid.NewGuid()}/permissions",
            new { permission = PermissionCatalog.OrdersRead });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokePermission_RemovesAndIsIdempotent()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();
        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.InventoryManage });
        grant.EnsureSuccessStatusCode();

        var revoke = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"admin/users/{userId}/permissions/{PermissionCatalog.InventoryManage}"));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = (await revoke.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.DoesNotContain(PermissionCatalog.InventoryManage, revoked.Permissions);

        var repeat = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"admin/users/{userId}/permissions/{PermissionCatalog.InventoryManage}"));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    }

    [Fact]
    public async Task GetUserPermissions_AndCatalog_ReturnLists()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();
        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.PaymentsStats });
        grant.EnsureSuccessStatusCode();

        var userPermissions = await _client.GetAsync($"admin/users/{userId}/permissions");
        Assert.Equal(HttpStatusCode.OK, userPermissions.StatusCode);
        var permissions = (await userPermissions.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<string>>>()).Data!;
        Assert.Contains(PermissionCatalog.PaymentsStats, permissions);

        var catalog = await _client.GetAsync("admin/users/permissions");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var catalogPermissions = (await catalog.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<string>>>()).Data!;
        Assert.Equal(PermissionCatalog.All.Count, catalogPermissions.Count);
        Assert.All(PermissionCatalog.All, permission => Assert.Contains(permission, catalogPermissions));
    }

    [Fact]
    public async Task PermissionCatalog_SuperAdmin_CanList_UsersRead_Cannot()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();

        var readerToken = jwtProvider.CreateAccessToken(
            Guid.NewGuid(), "reader@flux.local", ["Admin"], [PermissionCatalog.UsersRead]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);

        var denied = await _client.GetAsync("admin/users/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        AuthorizeAsSuperAdmin();

        var allowed = await _client.GetAsync("admin/users/permissions");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task GrantPermission_RequiresSuperAdmin_Returns403ForCustomer()
    {
        var userId = await RegisterAndVerifyUserAsync();
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var customerToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "cust@flux.local", ["Customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GrantedPermission_ReachesAccessToken_OnNextLogin()
    {
        var (userId, email) = await RegisterAndVerifyUserWithEmailAsync();
        AuthorizeAsSuperAdmin();
        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        grant.EnsureSuccessStatusCode();
        _client.DefaultRequestHeaders.Authorization = null;

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        login.EnsureSuccessStatusCode();
        var accessToken = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims;
        Assert.Contains(claims, claim =>
            claim.Type == PermissionCatalog.ClaimType && claim.Value == PermissionCatalog.OrdersRead);
    }

    private async Task<Guid> RegisterAndVerifyUserAsync()
    {
        return (await RegisterAndVerifyUserWithEmailAsync()).UserId;
    }

    private async Task<(Guid UserId, string Email)> RegisterAndVerifyUserWithEmailAsync()
    {
        var email = $"perm-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = "Str0ng!Pass1", firstName = "Perm", lastName = "Test" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        return (TestDatabase.GetUserId(_factory, email), email);
    }

    private void AuthorizeAsSuperAdmin()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var superAdminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "super@flux.local", ["SuperAdmin"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
    }
}
