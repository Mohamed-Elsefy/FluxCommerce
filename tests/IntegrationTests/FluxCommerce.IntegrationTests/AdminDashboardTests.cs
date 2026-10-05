using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluxCommerce.Api.Models;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the adaptive dashboard snapshot (Phase 22 E1): any authenticated user receives 200,
/// and every section is included only when entitled (null otherwise, never 403).
/// </summary>
[Collection(CheckoutTestCollection.Name)]
public class AdminDashboardTests
{
    private readonly CheckoutWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminDashboardTests(CheckoutWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetOrdersDatabase();
        _factory.CartSnapshotProvider.Clear();
        _factory.CartConsumption.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_SuperAdmin_ReturnsFullSnapshot()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-DASH-1", userId);
        AuthorizeAs(["SuperAdmin"], []);

        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
        Assert.True(data.Orders!.Total >= 1);
        Assert.True(data.Orders.Pending >= 1);
        Assert.True(data.Users!.Total >= 1);
        Assert.NotNull(data.Payments);
        Assert.NotNull(data.Inventory);
        Assert.NotNull(data.Catalog);
        Assert.Contains(data.RecentOrders, item => item.OrderId == created.OrderId);
    }

    [Fact]
    public async Task GetDashboard_CustomerWithoutPermissions_ReturnsEmptySections()
    {
        var userId = await RegisterAndLoginAsync();
        await CreateOrderForCurrentUserAsync("SKU-DASH-2", userId);

        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
        Assert.Null(data.Orders);
        Assert.Null(data.Payments);
        Assert.Null(data.Inventory);
        Assert.Null(data.Catalog);
        Assert.Null(data.Users);
        Assert.Empty(data.RecentOrders);
    }

    [Fact]
    public async Task GetDashboard_ScopedAdmin_ReturnsOnlyEntitledSections()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-DASH-3", userId);
        AuthorizeAs(["Admin"], [PermissionCatalog.OrdersRead]);

        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
        Assert.NotNull(data.Orders);
        Assert.Contains(data.RecentOrders, item => item.OrderId == created.OrderId);
        Assert.Null(data.Payments);
        Assert.Null(data.Inventory);
        Assert.Null(data.Catalog);
        Assert.Null(data.Users);
    }

    [Fact]
    public async Task GetDashboard_CatalogManage_SeesCatalogSectionOnly()
    {
        await RegisterAndLoginAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.CatalogManage]);

        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
        Assert.NotNull(data.Catalog);
        Assert.Null(data.Orders);
        Assert.Null(data.Payments);
        Assert.Null(data.Inventory);
        Assert.Null(data.Users);
        Assert.Empty(data.RecentOrders);
    }

    [Fact]
    public async Task GetDashboard_UsersRead_SeesUsersSectionOnly()
    {
        await RegisterAndLoginAsync();
        AuthorizeAs(["Admin"], [PermissionCatalog.UsersRead]);

        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
        Assert.NotNull(data.Users);
        Assert.True(data.Users.Total >= 1);
        Assert.Null(data.Orders);
        Assert.Null(data.Payments);
        Assert.Null(data.Inventory);
        Assert.Null(data.Catalog);
        Assert.Empty(data.RecentOrders);
    }

    [Fact]
    public async Task GetDashboard_InvalidThreshold_ReturnsBadRequest_WithoutEntitlement()
    {
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync("admin/dashboard?lowStockThreshold=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("admin/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private void AuthorizeAs(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var token = jwtProvider.CreateAccessToken(Guid.NewGuid(), "dash@flux.local", roles, permissions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<CheckoutResponse> CreateOrderForCurrentUserAsync(string sku, Guid userId)
    {
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, sku, 2, 5.00m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: sku,
                ProductName: "Wireless Mouse",
                UnitPriceAmount: 19.50m,
                Currency: "USD"));

        var request = new HttpRequestMessage(HttpMethod.Post, "orders/checkout")
        {
            Content = JsonContent.Create(ValidBody(cartId)),
        };
        request.Headers.Add("Idempotency-Key", $"checkout-{Guid.NewGuid():N}");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!;
    }

    private async Task<Guid> RegisterAndLoginAsync()
    {
        var email = $"admindash-{Guid.NewGuid():N}@example.com";
        const string password = "Str0ng!Pass1";

        var register = await _client.PostAsJsonAsync("auth/register", new { email, password, firstName = "Ada", lastName = "Lovelace" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var verificationToken = _factory.GetVerificationToken(email);
        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var login = await _client.PostAsJsonAsync("auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _factory.GetUserId(email);
    }

    private static CheckoutRequest ValidBody(Guid cartId) =>
        new(
            cartId,
            new CheckoutAddressRequest(
                "Ada Lovelace",
                "1 Analytical Engine Way",
                null,
                "London",
                "Greater London",
                "SW1A 1AA",
                "GB"),
            new CheckoutAddressRequest(
                "Ada Lovelace",
                "1 Analytical Engine Way",
                null,
                "London",
                "Greater London",
                "SW1A 1AA",
                "GB"));
}
