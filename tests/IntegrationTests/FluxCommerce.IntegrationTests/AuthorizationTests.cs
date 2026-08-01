using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class AuthorizationTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthorizationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AccessToken_ContainsIdentityAndRoleClaims()
    {
        var email = $"claims-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);
        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        login.EnsureSuccessStatusCode();
        var data = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!;

        var token = new JwtSecurityTokenHandler().ReadJwtToken(data.AccessToken);

        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == data.UserId.ToString());
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Email && c.Value == email);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Customer");
    }

    [Fact]
    public async Task AuthenticatedCustomer_CanAccess_ProtectedEndpoint()
    {
        var email = $"protected-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);
        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        var data = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!;

        _client.DefaultRequestHeaders.Authorization = new("Bearer", data.AccessToken);

        var response = await _client.GetAsync("auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_CanAccess_ProtectedEndpoint()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);
        var userId = TestDatabase.GetUserId(_factory, email);

        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(userId, email, ["Admin"]);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        var response = await _client.GetAsync("auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void RolePolicies_AreRegistered()
    {
        var options = _factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.GetPolicy("Customer"));
        Assert.NotNull(options.GetPolicy("Admin"));
        Assert.NotNull(options.GetPolicy("SuperAdmin"));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var token = jwtProvider.CreateAccessToken(Guid.NewGuid(), "expired@example.com", ["Customer"]);
        var expiredToken = MakeExpired(token);

        _client.DefaultRequestHeaders.Authorization = new("Bearer", expiredToken);

        var response = await _client.GetAsync("auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string MakeExpired(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var options = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));

        var expired = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audiences.First(),
            claims: jwt.Claims,
            expires: DateTime.UtcNow.AddMinutes(-30),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(expired);
    }

    private async Task RegisterAndVerify(string email)
    {
        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();
    }
}
