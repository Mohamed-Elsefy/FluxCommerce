using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.IntegrationTests;

public abstract class InventoryApiTestBase
{
    private const string Password = "Str0ng!Pass1";

    private readonly HttpClient _client;
    private string? _customerAccessToken;
    private string? _adminAccessToken;

    protected InventoryApiTestBase(InventoryWebApplicationFactory factory)
    {
        Factory = factory;
        _client = factory.CreateClient();
    }

    protected InventoryWebApplicationFactory Factory { get; }

    protected HttpClient Client => _client;

    /// <summary>Registers, verifies and logs in a fresh customer and attaches its bearer token.</summary>
    protected async Task AuthorizeAsync()
    {
        if (_customerAccessToken is not null)
            return;

        _customerAccessToken = await RegisterAndLoginAsync("customer");
    }

    /// <summary>Registers, verifies and logs in a fresh Admin-role account and attaches its bearer token.</summary>
    protected async Task AuthorizeAsAdminAsync()
    {
        if (_adminAccessToken is not null)
            return;

        _adminAccessToken = await RegisterAndLoginAsync("admin");
    }

    private async Task<string> RegisterAndLoginAsync(string role)
    {
        var email = $"inventory-{role}-{Guid.NewGuid():N}@example.com";

        if (role == "admin")
        {
            await Factory.RegisterAdminAsync(email, Password, "Inventory", "Admin");
        }
        else
        {
            var register = await _client.PostAsJsonAsync(
                "auth/register",
                new { email, password = Password, firstName = "Inventory", lastName = "Customer" });
            register.EnsureSuccessStatusCode();
        }

        var verificationToken = Factory.GetVerificationToken(email);

        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        verify.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var accessToken = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return accessToken;
    }

    protected static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}
