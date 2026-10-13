using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.Enums;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// End-to-end coverage of <c>POST /orders/checkout</c> through the real HTTP pipeline: JWT
/// authentication, the Checkout handler, atomic Order + idempotency persistence (SQLite Orders
/// database) and idempotent retry semantics. Cart and Catalog contracts are stubbed deterministically;
/// Identity validates real credentials.
/// </summary>
[Collection(CheckoutTestCollection.Name)]
public class CheckoutTests
{
    private readonly CheckoutWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutTests(CheckoutWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetOrdersDatabase();
        _factory.CartSnapshotProvider.Clear();
        _factory.CartConsumption.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Checkout_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("orders/checkout", ValidBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.CountOrders());
    }

    [Fact]
    public async Task Checkout_HappyPath_CreatesOrderPersistedAtomically()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-HAPPY", 2, 5.00m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-HAPPY",
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
        var data = (await response.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!;
        Assert.NotEqual(Guid.Empty, data.OrderId);
        Assert.False(string.IsNullOrEmpty(data.OrderNumber));
        Assert.Equal("Pending", data.Status);

        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CountIdempotencyRows());
    }

    [Fact]
    public async Task Checkout_HappyPath_ConsumesThePurchasedCartAfterCommit()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-CONSUME", 2, 5.00m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-CONSUME",
                ProductName: "Wireless Mouse",
                UnitPriceAmount: 19.50m,
                Currency: "USD"));

        var response = await PostCheckout(cartId, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CartConsumption.Calls);
        Assert.Equal(cartId, _factory.CartConsumption.LastCartId);
        Assert.Equal(userId, _factory.CartConsumption.LastCustomerId);
    }

    [Fact]
    public async Task Checkout_CartConsumptionFailure_OrderStillPersistsAtomically()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        _factory.CartConsumption.WithStatus(CartCheckoutConsumptionStatus.Failed);

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-CLEARFAIL", 1, 10m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-CLEARFAIL",
                ProductName: "Keyboard",
                UnitPriceAmount: 10m,
                Currency: "USD"));

        var response = await PostCheckout(cartId, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CountIdempotencyRows());
        Assert.Equal(1, _factory.CartConsumption.Calls);
    }

    [Fact]
    public async Task Checkout_RetryAfterCartConsumptionFailure_ReturnsSameOrderWithoutNewConsumption()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        const string key = "checkout-clear-fail-retry-key";
        _factory.CartConsumption.WithStatus(CartCheckoutConsumptionStatus.Failed);

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-RETRY", 1, 10m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-RETRY",
                ProductName: "Keyboard",
                UnitPriceAmount: 10m,
                Currency: "USD"));

        var first = await PostCheckout(cartId, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstId = (await first.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!.OrderId;
        Assert.Equal(1, _factory.CartConsumption.Calls);

        var retry = await PostCheckout(cartId, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryId = (await retry.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!.OrderId;

        Assert.Equal(firstId, retryId);
        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CountIdempotencyRows());
        Assert.Equal(1, _factory.CartConsumption.Calls);
    }

    [Fact]
    public async Task Checkout_RepeatedSameKeySameRequest_ReturnsOriginalOrder()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        const string key = "checkout-repeatable-key";

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-REP", 1, 10m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-REP",
                ProductName: "Keyboard",
                UnitPriceAmount: 10m,
                Currency: "USD"));

        var first = await PostCheckout(cartId, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstId = (await first.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!.OrderId;

        var second = await PostCheckout(cartId, key);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondId = (await second.ReadJsonAsync<ApiResponseDto<CheckoutResponse>>()).Data!.OrderId;

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CountIdempotencyRows());
    }

    [Fact]
    public async Task Checkout_SameKeyDifferentRequest_ReturnsConflict_WithoutDuplicateOrder()
    {
        var userId = await RegisterAndLoginAsync();
        var variantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        const string key = "checkout-conflict-key";

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), variantId, "SKU-CONF", 1, 10m, "USD"));
        _factory.CatalogCheckoutLookup.Seed(
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                ProductId: Guid.NewGuid(),
                Sku: "SKU-CONF",
                ProductName: "Keyboard",
                UnitPriceAmount: 10m,
                Currency: "USD"));

        var first = await PostCheckout(cartId, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var body = ValidBody(cartId) with
        {
            BillingAddress = new CheckoutAddressRequest(
                "Ada Lovelace",
                "1 Analytical Engine Way",
                null,
                "Manchester",
                "Manchester",
                "M1 1AA",
                "GB"),
        };

        var conflicting = await PostCheckout(body, key);

        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var problem = await conflicting.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Contains("Idempotency-Key", problem.Detail ?? string.Empty);
        Assert.Equal(1, _factory.CountOrders());
        Assert.Equal(1, _factory.CountIdempotencyRows());
    }

    [Fact]
    public async Task Checkout_EmptyCart_ReturnsBadRequest()
    {
        var userId = await RegisterAndLoginAsync();
        var cartId = Guid.NewGuid();
        _factory.CartSnapshotProvider.Seed(cartId, userId);

        var response = await PostCheckout(cartId, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.CountOrders());
    }

    [Fact]
    public async Task Checkout_MissingCart_ReturnsNotFound()
    {
        await RegisterAndLoginAsync();

        var response = await PostCheckout(Guid.NewGuid(), $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.CountOrders());
    }

    [Fact]
    public async Task Checkout_AnotherUsersCart_ReturnsForbidden()
    {
        var userId = await RegisterAndLoginAsync();
        var cartId = Guid.NewGuid();

        _factory.CartSnapshotProvider.Seed(
            cartId,
            customerId: Guid.NewGuid(),
            new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-OWNED", 1, 10m, "USD"));

        var response = await PostCheckout(cartId, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _factory.CountOrders());
    }

    [Fact]
    public async Task Checkout_UnavailableVariant_ReturnsConflict()
    {
        var userId = await RegisterAndLoginAsync();
        var cartId = Guid.NewGuid();

        _factory.CartSnapshotProvider.Seed(
            cartId,
            userId,
            new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-UNAVAIL", 1, 10m, "USD"));

        var response = await PostCheckout(cartId, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, _factory.CountOrders());
    }

    private async Task<HttpResponseMessage> PostCheckout(Guid cartId, string idempotencyKey) =>
        await PostCheckout(ValidBody(cartId), idempotencyKey);

    private async Task<HttpResponseMessage> PostCheckout(object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "orders/checkout")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> RegisterAndLoginAsync()
    {
        var email = $"checkout-{Guid.NewGuid():N}@example.com";
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