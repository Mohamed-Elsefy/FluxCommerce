using System.IdentityModel.Tokens.Jwt;
using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using ContractUserStatus = FluxCommerce.Modules.Identity.Contracts.Enums.UserStatus;
using DomainUserStatus = FluxCommerce.Modules.Identity.Domain.Enums.UserStatus;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class AuthFlowTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_Verify_Login_Refresh_Logout_FullJourney()
    {
        var email = $"journey-{Guid.NewGuid():N}@example.com";
        const string password = "Str0ng!Pass1";

        var register = await _client.PostAsJsonAsync("auth/register", new { email, password, firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        var verificationToken = TestDatabase.GetVerificationToken(_factory, email);

        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginData = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!;
        Assert.False(string.IsNullOrEmpty(loginData.AccessToken));
        Assert.False(string.IsNullOrEmpty(loginData.RefreshToken));
        Assert.Equal(email, loginData.Email);

        var me = await _client.GetAsync("auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", loginData.AccessToken);
        me = await _client.GetAsync("auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meData = (await me.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Equal(email, meData.Email);
        Assert.True(meData.IsEmailVerified);
        Assert.Equal("John", meData.FirstName);
        Assert.Equal("Doe", meData.LastName);

        var refresh = await _client.PostAsJsonAsync("auth/refresh", new { refreshToken = loginData.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = (await refresh.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!;
        Assert.NotEqual(loginData.RefreshToken, refreshed.RefreshToken);

        var logout = await _client.PostAsJsonAsync("auth/logout", new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var revoked = await _client.PostAsJsonAsync("auth/refresh", new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Registration_CreatesExactlyOneVerificationToken()
    {
        var email = $"token-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var token = TestDatabase.GetVerificationToken(_factory, email);
        Assert.False(string.IsNullOrEmpty(token));

        Assert.Equal(1, TestDatabase.CountVerificationTokenRows(_factory, email));
    }

    [Fact]
    public async Task Registration_SendsVerificationEmail_WithTokenMatchingDatabase()
    {
        var email = $"email-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var storedToken = TestDatabase.GetVerificationToken(_factory, email);
        var message = TestDatabase.GetLastEmail(_factory, email);

        Assert.Equal(email, message.ToEmail);
        Assert.Equal("Verify your FluxCommerce account", message.Subject);
        Assert.Contains($"https://localhost:4200/verify-email?token={storedToken}", message.HtmlBody);
        Assert.Contains($"https://localhost:4200/verify-email?token={storedToken}", message.TextBody);
        Assert.Contains("24 hours", message.HtmlBody);
    }

    [Fact]
    public async Task DuplicateRegistration_ReturnsConflict()
    {
        var email = $"duplicate-{Guid.NewGuid():N}@example.com";

        var first = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Registration_WithWeakPassword_ReturnsBadRequest()
    {
        var email = $"weak-{Guid.NewGuid():N}@example.com";

        var response = await _client.PostAsJsonAsync("auth/register", new { email, password = "weak", firstName = "John", lastName = "Doe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email, "Str0ng!Pass1");

        var response = await _client.PostAsJsonAsync("auth/login", new { email, password = "Wrong!Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_BeforeEmailVerification_ReturnsForbidden()
    {
        var email = $"unverified-{Guid.NewGuid():N}@example.com";
        await RegisterOnly(email, "Str0ng!Pass1");

        var response = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Account_IsLockedAfterRepeatedFailures()
    {
        var email = $"locked-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email, "Str0ng!Pass1");

        for (var i = 0; i < 5; i++)
        {
            var failed = await _client.PostAsJsonAsync("auth/login", new { email, password = "Wrong!Pass1" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });

        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.WwwAuthenticate.Count > 0);
    }

    [Fact]
    public async Task GarbageAccessToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-valid-token");

        var response = await _client.GetAsync("auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_Replay_ReturnsUnauthorized()
    {
        var email = $"replay-{Guid.NewGuid():N}@example.com";
        var loginData = await LoginAndGetTokens(email);

        var first = await _client.PostAsJsonAsync("auth/refresh", new { refreshToken = loginData.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await _client.PostAsJsonAsync("auth/refresh", new { refreshToken = loginData.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task VerificationToken_IsSingleUse()
    {
        var email = $"singleuse-{Guid.NewGuid():N}@example.com";
        await RegisterOnly(email, "Str0ng!Pass1");
        await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        var token = TestDatabase.GetVerificationToken(_factory, email);

        var first = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await _client.PostAsJsonAsync("auth/verify-email", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ResendVerification_IssuesNewToken()
    {
        var email = $"resend-{Guid.NewGuid():N}@example.com";
        await RegisterOnly(email, "Str0ng!Pass1");

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var originalToken = TestDatabase.GetVerificationToken(_factory, email);

        resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var resendData = (await resend.ReadJsonAsync<ApiResponseDto<ResendVerificationResponse>>()).Data!;
        Assert.True(resendData.EmailSent);

        var newToken = TestDatabase.GetVerificationToken(_factory, email);
        Assert.NotEqual(originalToken, newToken);

        var verifyOld = await _client.PostAsJsonAsync("auth/verify-email", new { token = originalToken });
        Assert.Equal(HttpStatusCode.BadRequest, verifyOld.StatusCode);

        var verifyNew = await _client.PostAsJsonAsync("auth/verify-email", new { token = newToken });
        Assert.Equal(HttpStatusCode.OK, verifyNew.StatusCode);
    }

    [Fact]
    public async Task Registration_CapturesNames_AndInitializesDefaults()
    {
        var email = $"defaults-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var data = (await register.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.Equal("John", data.FirstName);
        Assert.Equal("Doe", data.LastName);
        Assert.Equal("John Doe", data.DisplayName);
        Assert.Null(data.PhoneNumber);
        Assert.False(data.IsPhoneVerified);
        Assert.Null(data.AvatarUrl);
        Assert.Equal("en", data.PreferredLanguage);
        Assert.Equal("UTC", data.TimeZone);
        Assert.Equal(ContractUserStatus.Pending, data.Status);
        Assert.Equal(DomainUserStatus.Pending, TestDatabase.GetUserStatus(_factory, email));
    }

    [Fact]
    public async Task EmailVerification_ActivatesUser()
    {
        var email = $"activate-{Guid.NewGuid():N}@example.com";
        await RegisterOnly(email, "Str0ng!Pass1");
        Assert.Equal(DomainUserStatus.Pending, TestDatabase.GetUserStatus(_factory, email));

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var data = (await verify.ReadJsonAsync<ApiResponseDto<UserResponse>>()).Data!;
        Assert.True(data.IsEmailVerified);
        Assert.Equal(ContractUserStatus.Active, data.Status);
        Assert.Equal(DomainUserStatus.Active, TestDatabase.GetUserStatus(_factory, email));
    }

    [Fact]
    public async Task Registration_MissingNames_ReturnsBadRequest()
    {
        var email = $"noname-{Guid.NewGuid():N}@example.com";

        var response = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task RegisterOnly(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("auth/register", new { email, password, firstName = "John", lastName = "Doe" });
        response.EnsureSuccessStatusCode();
    }

    private async Task RegisterAndVerify(string email, string password)
    {
        await RegisterOnly(email, password);

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var response = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        response.EnsureSuccessStatusCode();
    }

    private async Task<LoginResponse> LoginAndGetTokens(string email)
    {
        await RegisterAndVerify(email, "Str0ng!Pass1");

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        login.EnsureSuccessStatusCode();

        return (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!;
    }
}
