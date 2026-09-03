using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class AdminControllerTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(Guid userId, string email)> RegisterAndVerifyUserAsync(string role = "Customer")
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "Test", lastName = "User" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        var userId = TestDatabase.GetUserId(_factory, email);

        // If role is not Customer, assign it via SuperAdmin (bypass HTTP, directly assign for setup)
        if (role != "Customer")
        {
            var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
            var superAdminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "super@flux.local", ["SuperAdmin"]);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            var assign = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role });
            // If role already Customer, this will add second role; ignore result for setup
            _client.DefaultRequestHeaders.Authorization = null;
        }

        return (userId, email);
    }

    private string CreateSuperAdminToken()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        return jwtProvider.CreateAccessToken(Guid.NewGuid(), "super@flux.local", ["SuperAdmin"]);
    }

    [Fact]
    public async Task AssignRole_Duplicate_Returns409Conflict()
    {
        var (userId, _) = await RegisterAndVerifyUserAsync("Customer");
        var superAdminToken = CreateSuperAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        // First assignment of Admin should succeed
        var first = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Duplicate assignment should return 409 Conflict
        var second = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UnlockUser_Returns200Ok()
    {
        var (userId, email) = await RegisterAndVerifyUserAsync();

        // Lock the user by 5 failed logins
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("auth/login", new { email, password = "WrongPass1!" });
        }

        // Verify user is locked (next login with correct password should be 423 or 403)
        // Now unlock via SuperAdmin
        var superAdminToken = CreateSuperAdminToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var unlock = await _client.PostAsJsonAsync($"admin/users/{userId}/unlock", new { });
        Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);

        // After unlock, login should succeed
        _client.DefaultRequestHeaders.Authorization = null;
        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task AssignRole_RequiresSuperAdmin_Returns403ForCustomer()
    {
        var (userId, _) = await RegisterAndVerifyUserAsync();
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var customerToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "cust@flux.local", ["Customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.PostAsJsonAsync($"admin/users/{userId}/roles", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unlock_RequiresSuperAdmin_Returns403ForAdmin()
    {
        var (userId, _) = await RegisterAndVerifyUserAsync();
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.PostAsJsonAsync($"admin/users/{userId}/unlock", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
