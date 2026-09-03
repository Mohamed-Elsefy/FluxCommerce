using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using Microsoft.Extensions.DependencyInjection;
using ContractsProductVisibility = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductVisibility;

namespace FluxCommerce.IntegrationTests;

public abstract class CatalogApiTestBase
{
    private const string Password = "Str0ng!Pass1";

    private readonly HttpClient _client;
    private string? _accessToken;

    protected CatalogApiTestBase(CatalogWebApplicationFactory factory)
    {
        Factory = factory;
        _client = factory.CreateClient();

        CatalogDatabase.ResetCatalogDatabase(factory);
    }

    protected CatalogWebApplicationFactory Factory { get; }

    protected HttpClient Client => _client;

    protected async Task AuthorizeAsync()
    {
        if (_accessToken is not null)
            return;

        // Catalog mutations now require AdminPolicy (Phase D). Create an Admin JWT directly
        // (role claim is sufficient for Authorization, no DB user needed for test speed).
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminEmail = $"catalog-admin-{Guid.NewGuid():N}@example.com";
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), adminEmail, ["Admin"]);
        _accessToken = adminToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        await Task.CompletedTask;
    }

    protected async Task AuthorizeAsCustomerAsync()
    {
        if (_accessToken is not null)
            return;

        var email = $"catalog-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = Password, firstName = "Catalog", lastName = "Tester" });
        register.EnsureSuccessStatusCode();

        var verificationToken = CatalogDatabase.GetVerificationToken(Factory, email);

        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        verify.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        _accessToken = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    protected async Task<Guid> CreateCategoryAsync(
        string name,
        string? slug = null,
        string? description = null,
        Guid? parentId = null)
    {
        await AuthorizeAsync();

        var response = await _client.PostAsJsonAsync(
            "catalog/categories",
            new { name, slug = slug ?? Slugify(name), description, parentId });
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }

    protected async Task<Guid> CreateProductAsync(
        Guid categoryId,
        string name,
        string? slug = null,
        string? description = null,
        ContractsProductVisibility visibility = ContractsProductVisibility.Visible)
    {
        await AuthorizeAsync();

        var response = await _client.PostAsJsonAsync(
            "catalog/products",
            new { name, slug = slug ?? Slugify(name), description, categoryId, visibility });
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }

    protected static string Slugify(string name) =>
        name.ToLowerInvariant().Replace(' ', '-');
}
