using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class PasswordResetFlowTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PasswordResetFlowTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ForgotAndResetPassword_FullFlow()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);

        var forgot = await _client.PostAsJsonAsync("auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var resetToken = TestDatabase.GetPasswordResetToken(_factory, email);

        var reset = await _client.PostAsJsonAsync(
            "auth/reset-password",
            new { token = resetToken, newPassword = "New!Pass99" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var oldPasswordLogin = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await _client.PostAsJsonAsync("auth/login", new { email, password = "New!Pass99" });
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_TokenIsSingleUse()
    {
        var email = $"resetreplay-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);
        await _client.PostAsJsonAsync("auth/forgot-password", new { email });
        var resetToken = TestDatabase.GetPasswordResetToken(_factory, email);

        var first = await _client.PostAsJsonAsync(
            "auth/reset-password",
            new { token = resetToken, newPassword = "New!Pass99" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await _client.PostAsJsonAsync(
            "auth/reset-password",
            new { token = resetToken, newPassword = "Other!Pass99" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithUnknownToken_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(
            "auth/reset-password",
            new { token = "unknown-token", newPassword = "New!Pass99" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_SendsResetEmail_WithTokenMatchingDatabase()
    {
        var email = $"resetmail-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(email);

        var forgot = await _client.PostAsJsonAsync("auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var storedToken = TestDatabase.GetPasswordResetToken(_factory, email);
        var message = TestDatabase.GetLastEmail(_factory, email);

        Assert.Equal(email, message.ToEmail);
        Assert.Equal("Reset your FluxCommerce password", message.Subject);
        Assert.Contains($"https://localhost:4200/reset-password?token={storedToken}", message.HtmlBody);
        Assert.Contains($"https://localhost:4200/reset-password?token={storedToken}", message.TextBody);
        Assert.Contains("1 hour", message.HtmlBody);
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync(
            "auth/forgot-password",
            new { email = $"missing-{Guid.NewGuid():N}@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_WithInvalidEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("auth/forgot-password", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task RegisterAndVerify(string email)
    {
        var register = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        register.EnsureSuccessStatusCode();

        var resend = await _client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();
    }
}
