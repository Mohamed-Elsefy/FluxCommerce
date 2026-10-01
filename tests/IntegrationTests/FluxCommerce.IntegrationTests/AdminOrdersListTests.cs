using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// End-to-end coverage of the administrative Orders surface through the real HTTP pipeline:
/// <c>GET /admin/orders</c> (list with status/search filters), <c>GET /admin/orders/counts</c>
/// and <c>GET /admin/orders/{orderId}</c>. Orders are produced through the same Checkout flow
/// the CheckoutTests exercise; cross-customer visibility and Admin-only authorization are asserted.
/// </summary>
[Collection(CheckoutTestCollection.Name)]
public class AdminOrdersListTests
{
    private readonly CheckoutWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminOrdersListTests(CheckoutWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetOrdersDatabase();
        _factory.CartSnapshotProvider.Clear();
        _factory.CartConsumption.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAdminOrders_ReturnsOrdersAcrossCustomers()
    {
        var firstUserId = await RegisterAndLoginAsync();
        var first = await CreateOrderForCurrentUserAsync("SKU-ADM-1", firstUserId);
        var secondUserId = await RegisterAndLoginAsync();
        var second = await CreateOrderForCurrentUserAsync("SKU-ADM-2", secondUserId);

        AuthorizeAsAdmin();

        var response = await _client.GetAsync("admin/orders?pageNumber=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.True(data.TotalCount >= 2);
        Assert.Contains(data.Items, item => item.OrderId == first.OrderId);
        Assert.Contains(data.Items, item => item.OrderId == second.OrderId);
    }

    [Fact]
    public async Task GetAdminOrders_AppliesStatusAndSearchFilters()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-3", userId);
        AuthorizeAsAdmin();

        var pending = await _client.GetAsync("admin/orders?status=Pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingData = (await pending.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.Contains(pendingData.Items, item => item.OrderId == created.OrderId);

        var confirmed = await _client.GetAsync("admin/orders?status=Confirmed");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedData = (await confirmed.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.DoesNotContain(confirmedData.Items, item => item.OrderId == created.OrderId);

        var details = await _client.GetAsync($"admin/orders/{created.OrderId}");
        details.EnsureSuccessStatusCode();
        var orderNumber = (await details.ReadJsonAsync<ApiResponseDto<OrderDetailsResponse>>()).Data!.OrderNumber;

        var search = await _client.GetAsync($"admin/orders?search={orderNumber[..6]}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchData = (await search.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.Contains(searchData.Items, item => item.OrderId == created.OrderId);
    }

    [Fact]
    public async Task GetAdminOrderCounts_ReflectsCreatedOrders()
    {
        var userId = await RegisterAndLoginAsync();
        await CreateOrderForCurrentUserAsync("SKU-ADM-4", userId);
        AuthorizeAsAdmin();

        var response = await _client.GetAsync("admin/orders/counts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<AdminOrderCountsResponse>>()).Data!;
        Assert.True(data.Total >= 1);
        Assert.True(data.Pending >= 1);
        Assert.Equal(data.Total, data.Pending + data.Confirmed + data.Cancelled);
    }

    [Fact]
    public async Task GetAdminOrder_ReturnsDetails_And404ForUnknown()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-5", userId);
        AuthorizeAsAdmin();

        var found = await _client.GetAsync($"admin/orders/{created.OrderId}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var payload = (await found.ReadJsonAsync<ApiResponseDto<OrderDetailsResponse>>()).Data!;
        Assert.Equal(created.OrderId, payload.OrderId);
        Assert.Equal(created.OrderNumber, payload.OrderNumber);

        var missing = await _client.GetAsync($"admin/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetAdminOrders_RequiresAdmin_Returns403ForCustomer()
    {
        var userId = await RegisterAndLoginAsync();
        await CreateOrderForCurrentUserAsync("SKU-ADM-6", userId);

        var response = await _client.GetAsync("admin/orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyOrders_AppliesStatusFilter()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-7", userId);

        var pending = await _client.GetAsync("orders?status=Pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingData = (await pending.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.Contains(pendingData.Items, item => item.OrderId == created.OrderId);

        var confirmed = await _client.GetAsync("orders?status=Confirmed");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmedData = (await confirmed.ReadJsonAsync<ApiResponseDto<PagedResponseDto<OrderSummaryResponse>>>()).Data!;
        Assert.DoesNotContain(confirmedData.Items, item => item.OrderId == created.OrderId);

        var invalid = await _client.GetAsync("orders?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task CancelOrderAsAdmin_CancelsAnyPendingOrder()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-8", userId);
        AuthorizeAsAdmin();

        var cancel = await _client.PostAsync($"admin/orders/{created.OrderId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var payload = (await cancel.ReadJsonAsync<ApiResponseDto<OrderDetailsResponse>>()).Data!;
        Assert.Equal("Cancelled", payload.Status);

        var repeat = await _client.PostAsync($"admin/orders/{created.OrderId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);

        var missing = await _client.PostAsync($"admin/orders/{Guid.NewGuid()}/cancel", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task CancelOrderAsAdmin_RequiresAdmin_Returns403ForCustomer()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-9", userId);

        var response = await _client.PostAsync($"admin/orders/{created.OrderId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequestRefund_PendingOrder_ReturnsConflict_UnknownReturnsNotFound()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-ADM-10", userId);
        AuthorizeAsAdmin();

        var pending = await _client.PostAsJsonAsync(
            $"admin/orders/{created.OrderId}/refund",
            new { reason = "Customer asked for a refund." });
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);

        var missing = await _client.PostAsJsonAsync(
            $"admin/orders/{Guid.NewGuid()}/refund",
            new { reason = "Customer asked for a refund." });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var blankReason = await _client.PostAsJsonAsync(
            $"admin/orders/{created.OrderId}/refund",
            new { reason = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, blankReason.StatusCode);
    }

    private void AuthorizeAsAdmin()
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
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
        var email = $"adminorders-{Guid.NewGuid():N}@example.com";
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
