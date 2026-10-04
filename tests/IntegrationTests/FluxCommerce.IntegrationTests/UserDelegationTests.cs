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
/// Covers delegated user management (ADR-017, Phase E3): `users.read` opens reads only,
/// `users.manage` opens writes under anti-escalation guards, and `admins.create` opens
/// admin registration. SuperAdmin stays exempt from every guard.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public class UserDelegationTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserDelegationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UsersRead_CanList_ButCannotMutate()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersRead]);

        var list = await _client.GetAsync("admin/users?search=nobody-will-match-this");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var details = await _client.GetAsync($"admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        var mutate = await _client.PostAsJsonAsync($"admin/users/{userId}/unlock", new { });
        Assert.Equal(HttpStatusCode.Forbidden, mutate.StatusCode);

        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task UsersManage_CanUnlock_AndGrantHeldPermissions()
    {
        var (userId, email) = await RegisterAndVerifyUserWithEmailAsync();
        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("auth/login", new { email, password = "WrongPass1!" });

        AuthorizeAs(["Admin"], [PermissionCatalog.UsersManage, PermissionCatalog.OrdersRead]);

        var unlock = await _client.PostAsJsonAsync($"admin/users/{userId}/unlock", new { });
        Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);

        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var granted = (await grant.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Contains(PermissionCatalog.OrdersRead, granted.Permissions);
    }

    [Fact]
    public async Task UsersManage_CannotGrantUnheldPermission()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersManage]);

        var response = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersManage_CannotAssignSuperAdminRole()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersManage]);

        var response = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/roles",
            new { role = "SuperAdmin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersManage_CannotTouchSuperAdminAccount()
    {
        var superAdminId = await RegisterPromoteToSuperAdminAsync();
        AuthorizeAs(["Admin"], PermissionCatalog.AdminBundle);

        var unlock = await _client.PostAsJsonAsync($"admin/users/{superAdminId}/unlock", new { });
        Assert.Equal(HttpStatusCode.Forbidden, unlock.StatusCode);

        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{superAdminId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task AdminsCreate_CanRegisterAdmin()
    {
        AuthorizeAs(["Admin"], [PermissionCatalog.AdminsCreate]);

        var email = $"delegated-{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync(
            "auth/register-admin",
            new { email, password = "Str0ng!Pass1", firstName = "Delegated", lastName = "Admin" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AdminsCreate_WithoutPermission_Returns403()
    {
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersRead]);

        var response = await _client.PostAsJsonAsync(
            "auth/register-admin",
            new { email = $"delegated-{Guid.NewGuid():N}@example.com", password = "Str0ng!Pass1", firstName = "Delegated", lastName = "Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> RegisterAndVerifyUserAsync()
    {
        return (await RegisterAndVerifyUserWithEmailAsync()).UserId;
    }

    private async Task<(Guid UserId, string Email)> RegisterAndVerifyUserWithEmailAsync()
    {
        var email = $"deleg-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = "Str0ng!Pass1", firstName = "Deleg", lastName = "Ated" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        return (TestDatabase.GetUserId(_factory, email), email);
    }

    private async Task<Guid> RegisterPromoteToSuperAdminAsync()
    {
        var userId = await RegisterAndVerifyUserAsync();

        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var superAdminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "super@flux.local", ["SuperAdmin"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var assign = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role = "SuperAdmin" });
        assign.EnsureSuccessStatusCode();

        _client.DefaultRequestHeaders.Authorization = null;
        return userId;
    }

    private void AuthorizeAs(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var token = jwtProvider.CreateAccessToken(Guid.NewGuid(), "delegated@flux.local", roles, permissions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
