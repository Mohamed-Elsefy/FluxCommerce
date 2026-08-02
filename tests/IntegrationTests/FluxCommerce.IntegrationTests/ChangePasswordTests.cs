using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class ChangePasswordTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChangePasswordTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChangePassword_FullFlow()
    {
        var email = $"changepw-{Guid.NewGuid():N}@example.com";
        var accessToken = await RegisterVerifyAndLogin(email);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var wrongCurrent = await _client.PostAsJsonAsync(
            "auth/change-password",
            new { currentPassword = "Wrong!Pass1", newPassword = "New!Pass99", confirmPassword = "New!Pass99" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var change = await _client.PostAsJsonAsync(
            "auth/change-password",
            new { currentPassword = "Str0ng!Pass1", newPassword = "New!Pass99", confirmPassword = "New!Pass99" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
        var oldPasswordLogin = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await _client.PostAsJsonAsync("auth/login", new { email, password = "New!Pass99" });
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "auth/change-password",
            new { currentPassword = "Str0ng!Pass1", newPassword = "New!Pass99", confirmPassword = "New!Pass99" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithMismatchedConfirmation_ReturnsBadRequest()
    {
        var email = $"mismatch-{Guid.NewGuid():N}@example.com";
        var accessToken = await RegisterVerifyAndLogin(email);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync(
            "auth/change-password",
            new { currentPassword = "Str0ng!Pass1", newPassword = "New!Pass99", confirmPassword = "Other!Pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> RegisterVerifyAndLogin(string email)
    {
        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        login.EnsureSuccessStatusCode();

        return (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;
    }
}
