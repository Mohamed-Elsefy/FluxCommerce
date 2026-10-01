using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the customer profile update: <c>PUT /auth/profile</c> applies partial updates,
/// rejects invalid input and requires authentication.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public class UpdateProfileTests
{
    private const string Password = "Str0ng!Pass1";

    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UpdateProfileTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UpdateProfile_PartialUpdate_ReturnsUpdatedUser()
    {
        await RegisterLoginAsync();

        var response = await _client.PutAsJsonAsync(
            "auth/profile",
            new { firstName = "Augusta", phoneNumber = "+49123456789" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Equal("Augusta", payload.FirstName);
        Assert.Equal("Lovelace", payload.LastName);
        Assert.Equal("+49123456789", payload.PhoneNumber);
        Assert.Equal("Augusta Lovelace", payload.DisplayName);
    }

    [Fact]
    public async Task UpdateProfile_ClearOptionalField_SetsNull()
    {
        await RegisterLoginAsync();
        var set = await _client.PutAsJsonAsync("auth/profile", new { phoneNumber = "+49123456789" });
        set.EnsureSuccessStatusCode();

        var clear = await _client.PutAsJsonAsync("auth/profile", new { phoneNumber = "" });

        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var payload = (await clear.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Null(payload.PhoneNumber);
    }

    [Fact]
    public async Task UpdateProfile_InvalidInput_ReturnsBadRequest()
    {
        await RegisterLoginAsync();

        var blankName = await _client.PutAsJsonAsync("auth/profile", new { firstName = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);

        var tooLong = await _client.PutAsJsonAsync("auth/profile", new { firstName = new string('A', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PutAsJsonAsync("auth/profile", new { firstName = "Nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task RegisterLoginAsync()
    {
        var email = $"profile-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = Password, firstName = "Ada", lastName = "Lovelace" });
        register.EnsureSuccessStatusCode();

        var verificationToken = TestDatabase.GetVerificationToken(_factory, email);

        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        verify.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var accessToken = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
