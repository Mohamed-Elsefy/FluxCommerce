using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the administrative user management surface: user list with status/role/search
/// filters, single user lookup, aggregate counts and SuperAdmin-only authorization.
/// The shared collection database is used, so assertions are containment-based with
/// test-unique emails.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public class AdminUsersListTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminUsersListTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndVerifyUserAsync()
    {
        var email = $"adminlist-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = "Str0ng!Pass1", firstName = "Admin", lastName = "List" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        return email;
    }

    private string CreateSuperAdminToken()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        return jwtProvider.CreateAccessToken(Guid.NewGuid(), "super@flux.local", ["SuperAdmin"]);
    }

    private void AuthorizeAsSuperAdmin() =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateSuperAdminToken());

    [Fact]
    public async Task GetUsers_ReturnsRegisteredUser_AndAppliesSearchFilter()
    {
        var email = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var unfiltered = await _client.GetAsync("admin/users?pageNumber=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
        var page = await unfiltered.ReadJsonAsync<ApiResponseDto<PagedResponseDto<UserAdminResponse>>>();
        Assert.True(page.IsSuccess);
        Assert.Contains(page.Data!.Items, user => user.Email == email);

        var filtered = await _client.GetAsync($"admin/users?search={email}");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var filteredPage = await filtered.ReadJsonAsync<ApiResponseDto<PagedResponseDto<UserAdminResponse>>>();
        Assert.True(filteredPage.IsSuccess);
        Assert.Single(filteredPage.Data!.Items);
        Assert.Equal(email, filteredPage.Data.Items[0].Email);
        Assert.NotNull(filteredPage.Data.Items[0].Permissions);
        Assert.Empty(filteredPage.Data.Items[0].Permissions);
    }

    [Fact]
    public async Task GetUsers_AppliesRoleFilter()
    {
        var email = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var asCustomer = await _client.GetAsync($"admin/users?search={email}&role=Customer");
        Assert.Equal(HttpStatusCode.OK, asCustomer.StatusCode);
        var customerPage = await asCustomer.ReadJsonAsync<ApiResponseDto<PagedResponseDto<UserAdminResponse>>>();
        Assert.True(customerPage.IsSuccess);
        Assert.Contains(customerPage.Data!.Items, user => user.Email == email);

        var asAdmin = await _client.GetAsync($"admin/users?search={email}&role=Admin");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        var adminPage = await asAdmin.ReadJsonAsync<ApiResponseDto<PagedResponseDto<UserAdminResponse>>>();
        Assert.True(adminPage.IsSuccess);
        Assert.DoesNotContain(adminPage.Data!.Items, user => user.Email == email);
    }

    [Fact]
    public async Task GetUser_Returns200_And404ForUnknown()
    {
        var email = await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var userId = TestDatabase.GetUserId(_factory, email);
        var found = await _client.GetAsync($"admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var payload = await found.ReadJsonAsync<ApiResponseDto<UserAdminResponse>>();
        Assert.True(payload.IsSuccess);
        Assert.Equal(email, payload.Data!.Email);
        Assert.NotNull(payload.Data.Permissions);
        Assert.Empty(payload.Data.Permissions);

        var missing = await _client.GetAsync($"admin/users/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetUserCounts_ReflectsRegisteredUsers()
    {
        await RegisterAndVerifyUserAsync();
        AuthorizeAsSuperAdmin();

        var response = await _client.GetAsync("admin/users/counts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<UserAdminCountsResponse>>();
        Assert.True(payload.IsSuccess);
        Assert.True(payload.Data!.Total >= 1);
        Assert.True(payload.Data.Customers >= 1);
    }

    [Fact]
    public async Task GetUsers_RequiresSuperAdmin_Returns403ForCustomer()
    {
        await RegisterAndVerifyUserAsync();
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var customerToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "cust@flux.local", ["Customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.GetAsync("admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
