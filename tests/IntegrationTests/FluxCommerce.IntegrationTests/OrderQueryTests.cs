using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// End-to-end coverage of the customer-facing Orders read and lifecycle endpoints through the real
/// HTTP pipeline: <c>GET /orders</c>, <c>GET /orders/{orderId}</c> and
/// <c>POST /orders/{orderId}/cancel</c>. Orders are produced through the same Checkout flow the
/// CheckoutTests exercise, and ownership is asserted across two registered customers.
/// </summary>
[Collection(CheckoutTestCollection.Name)]
public class OrderQueryTests
{
    private readonly CheckoutWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrderQueryTests(CheckoutWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetOrdersDatabase();
        _factory.CartSnapshotProvider.Clear();
        _factory.CartConsumption.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetMyOrders_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyOrders_NoOrders_ReturnsEmptyPage()
    {
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync("orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedDto<OrderSummaryResponse>>>()).Data!;
        Assert.Empty(data.Items);
        Assert.Equal(0, data.TotalCount);
        Assert.Equal(1, data.PageNumber);
        Assert.Equal(20, data.PageSize);
    }

    [Fact]
    public async Task GetMyOrders_HappyPath_ReturnsOwnedOrdersNewestFirst()
    {
        var userId = await RegisterAndLoginAsync();
        var first = await CreateOrderForCurrentUserAsync("SKU-LIST-1", userId);
        var second = await CreateOrderForCurrentUserAsync("SKU-LIST-2", userId);

        var response = await _client.GetAsync("orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedDto<OrderSummaryResponse>>>()).Data!;
        Assert.Equal(2, data.TotalCount);
        Assert.Equal(2, data.Items.Count);
        Assert.Equal(second.OrderId, data.Items[0].OrderId);
        Assert.Equal(first.OrderId, data.Items[1].OrderId);
        Assert.Equal("Pending", data.Items[0].Status);
        Assert.Equal(39.00m, data.Items[0].GrandTotal.Amount);
        Assert.Equal("USD", data.Items[0].GrandTotal.Currency);
        Assert.Equal(1, data.Items[0].ItemCount);
    }

    [Fact]
    public async Task GetMyOrders_OnlyReturnsTheCallersOwnOrders()
    {
        var firstUser = await RegisterAndLoginAsync();
        var firstUserOrder = await CreateOrderForCurrentUserAsync("SKU-OWNED-1", firstUser);
        var secondUserOrder = await CreateOrderForCurrentUserAsync("SKU-OWNED-2", firstUser);

        await RegisterAndLoginAsync();
        var otherOrder = await CreateOrderAsync("SKU-OWNED-3");

        var response = await _client.GetAsync("orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedDto<OrderSummaryResponse>>>()).Data!;
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(otherOrder.OrderId, Assert.Single(data.Items).OrderId);
        Assert.DoesNotContain(data.Items, item => item.OrderId == firstUserOrder.OrderId);
        Assert.DoesNotContain(data.Items, item => item.OrderId == secondUserOrder.OrderId);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetMyOrders_InvalidPagination_ReturnsBadRequest(int pageNumber, int pageSize)
    {
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync($"orders?pageNumber={pageNumber}&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.False(string.IsNullOrEmpty(problem.Detail));
    }

    [Fact]
    public async Task GetOrder_HappyPath_ReturnsFullDetails()
    {
        var created = await CreateOrderAsync("SKU-DETAILS");

        var response = await _client.GetAsync($"orders/{created.OrderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = (await response.ReadJsonAsync<ApiResponseDto<OrderDetailsResponse>>()).Data!;
        Assert.Equal(created.OrderId, details.OrderId);
        Assert.Equal(created.OrderNumber, details.OrderNumber);
        Assert.Equal("Pending", details.Status);
        Assert.Equal(39.00m, details.GrandTotal.Amount);
        Assert.Equal(39.00m, details.Subtotal.Amount);
        Assert.Equal(0m, details.ShippingCost.Amount);
        Assert.Equal(0m, details.Discount.Amount);
        Assert.Equal(1, details.ItemCount);
        Assert.Single(details.LineItems);
        Assert.Equal("Wireless Mouse", details.LineItems[0].ProductName);
        Assert.Equal("SKU-DETAILS", details.LineItems[0].Sku);
        Assert.Equal(2, details.LineItems[0].Quantity);
        Assert.Equal(19.50m, details.LineItems[0].UnitPrice);
        Assert.Equal(39.00m, details.LineItems[0].LineTotal);
        Assert.Equal("Ada Lovelace", details.ShippingAddress.RecipientName);
        Assert.Equal("GB", details.BillingAddress.CountryCode);
    }

    [Fact]
    public async Task GetOrder_AnotherUsersOrder_ReturnsNotFound()
    {
        var created = await CreateOrderAsync("SKU-GATE-1");
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync($"orders/{created.OrderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_MissingOrder_ReturnsNotFound()
    {
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync($"orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelOrder_PendingOrder_CancelsAndPersists()
    {
        var created = await CreateOrderAsync("SKU-CANCEL");

        var response = await _client.PostAsync($"orders/{created.OrderId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = (await response.ReadJsonAsync<ApiResponseDto<OrderDetailsResponse>>()).Data!;
        Assert.Equal(created.OrderId, details.OrderId);
        Assert.Equal("Cancelled", details.Status);
    }

    [Fact]
    public async Task CancelOrder_AlreadyCancelled_ReturnsConflict()
    {
        var created = await CreateOrderAsync("SKU-CANCEL-2");
        var first = await _client.PostAsync($"orders/{created.OrderId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsync($"orders/{created.OrderId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.ReadJsonAsync<ProblemDetailsDto>();
        Assert.False(string.IsNullOrEmpty(problem.Detail));
    }

    [Fact]
    public async Task CancelOrder_AnotherUsersOrder_ReturnsNotFound_WithoutChangingIt()
    {
        var created = await CreateOrderAsync("SKU-CANCEL-3");
        await RegisterAndLoginAsync();

        var response = await _client.PostAsync($"orders/{created.OrderId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Pending", _factory.GetOrderStatus(created.OrderId));
    }

    [Fact]
    public async Task CancelOrder_MissingOrder_ReturnsNotFound()
    {
        await RegisterAndLoginAsync();

        var response = await _client.PostAsync($"orders/{Guid.NewGuid()}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<CheckoutResponse> CreateOrderAsync(string sku)
    {
        var userId = await RegisterAndLoginAsync();
        return await CreateOrderForCurrentUserAsync(sku, userId);
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
        var email = $"orders-{Guid.NewGuid():N}@example.com";
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

    private sealed record PagedDto<T>(
        IReadOnlyList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize);
}
