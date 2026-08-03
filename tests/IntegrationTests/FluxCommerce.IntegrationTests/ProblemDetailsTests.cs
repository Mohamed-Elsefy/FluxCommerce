using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class ProblemDetailsTests
{
    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProblemDetailsTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsProblemDetails()
    {
        var email = $"problem-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(_client, email);

        var response = await _client.PostAsJsonAsync("auth/login", new { email, password = "Wrong!Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType?.ToString());

        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("https://tools.ietf.org/html/rfc7235#section-3.1", problem.Type);
        Assert.Equal("Unauthorized", problem.Title);
        Assert.Equal(401, problem.Status);
        Assert.Equal("The email or password is incorrect.", problem.Detail);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameProblemDetails_AsWrongPassword()
    {
        var wrongPassword = await _client.PostAsJsonAsync(
            "auth/login",
            new { email = $"enum-{Guid.NewGuid():N}@example.com", password = "Str0ng!Pass1" });

        var unknownEmail = await _client.PostAsJsonAsync(
            "auth/login",
            new { email = $"enum-{Guid.NewGuid():N}@example.com", password = "Wrong!Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        var first = await wrongPassword.ReadJsonAsync<ProblemDetailsDto>();
        var second = await unknownEmail.ReadJsonAsync<ProblemDetailsDto>();

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Detail, second.Detail);
        Assert.Equal(first.Title, second.Title);
    }

    [Fact]
    public async Task DuplicateRegistration_ReturnsProblemDetails()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });

        var response = await _client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains("already exists", problem.Detail);
    }

    [Fact]
    public async Task Registration_WithWeakPassword_ReturnsProblemDetails()
    {
        var response = await _client.PostAsJsonAsync(
            "auth/register",
            new { email = $"weak-{Guid.NewGuid():N}@example.com", password = "weak", firstName = "John", lastName = "Doe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal(400, problem.Status);
        Assert.Equal("Validation Failed", problem.Title);
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Login_WhenLocked_ReturnsForbidden_ProblemDetails()
    {
        var email = $"lockedpd-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(_client, email);

        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("auth/login", new { email, password = "Wrong!Pass1" });
        }

        var response = await _client.PostAsJsonAsync("auth/login", new { email, password = "Str0ng!Pass1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal(403, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.Contains("locked", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyEmail_WithInvalidToken_ReturnsProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("auth/verify-email", new { token = "invalid-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal(400, problem.Status);
        Assert.Contains("token", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsLocalizedProblemDetails_ForFrenchRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr");

        var email = $"l10n-{Guid.NewGuid():N}@example.com";
        await RegisterAndVerify(client, email);

        var response = await client.PostAsJsonAsync("auth/login", new { email, password = "Wrong!Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Non autorisé", problem.Title);
        Assert.Equal("Adresse e-mail ou mot de passe incorrect.", problem.Detail);
    }

    private async Task RegisterAndVerify(HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("auth/register", new { email, password = "Str0ng!Pass1", firstName = "John", lastName = "Doe" });
        register.EnsureSuccessStatusCode();

        var resend = await client.PostAsJsonAsync("auth/resend-verification", new { email });
        resend.EnsureSuccessStatusCode();

        var token = TestDatabase.GetVerificationToken(_factory, email);
        var verify = await client.PostAsJsonAsync("auth/verify-email", new { token });
        verify.EnsureSuccessStatusCode();
    }
}
