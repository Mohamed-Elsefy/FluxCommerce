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
/// Covers users.read enforcement: holders can read users/counts/details/permission-values but
/// cannot mutate anything (roles, unlock, grant, revoke, admin creation stay SuperAdmin-only),
/// and permission-less callers are rejected from reads as well.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public class UsersReadEnforcementTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsersReadEnforcementTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UsersRead_CanRead_AllUserSurfaces()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersRead]);

        var list = await _client.GetAsync("admin/users?search=nobody-will-match-this");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var counts = await _client.GetAsync("admin/users/counts");
        Assert.Equal(HttpStatusCode.OK, counts.StatusCode);

        var details = await _client.GetAsync($"admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var payload = (await details.ReadJsonAsync<ApiResponseDto<UserAdminResponse>>()).Data!;
        Assert.Equal(userId, payload.Id);

        var permissions = await _client.GetAsync($"admin/users/{userId}/permissions");
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
    }

    [Fact]
    public async Task UsersRead_CannotMutate_AnyWriteSurface()
    {
        var userId = await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersRead]);

        var assign = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);

        var unlock = await _client.PostAsJsonAsync($"admin/users/{userId}/unlock", new { });
        Assert.Equal(HttpStatusCode.Forbidden, unlock.StatusCode);

        var grant = await _client.PostAsJsonAsync(
            $"admin/users/{userId}/permissions",
            new { permission = PermissionCatalog.OrdersRead });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);

        var revoke = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"admin/users/{userId}/permissions/{PermissionCatalog.OrdersRead}"));
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);

        var register = await _client.PostAsJsonAsync(
            "auth/register-admin",
            new { email = $"delegated-{Guid.NewGuid():N}@example.com", password = "Str0ng!Pass1", firstName = "Delegated", lastName = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);

        var catalog = await _client.GetAsync("admin/users/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
    }

    [Fact]
    public async Task WithoutUsersRead_CannotReadUsers()
    {
        await RegisterAndVerifyUserAsync();
        AuthorizeAs(["Admin"], []);

        var response = await _client.GetAsync("admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> RegisterAndVerifyUserAsync()
    {
        var email = $"uread-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = "Str0ng!Pass1", firstName = "Uread", lastName = "Test" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        return TestDatabase.GetUserId(_factory, email);
    }

    private void AuthorizeAs(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var token = jwtProvider.CreateAccessToken(Guid.NewGuid(), "uread@flux.local", roles, permissions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
