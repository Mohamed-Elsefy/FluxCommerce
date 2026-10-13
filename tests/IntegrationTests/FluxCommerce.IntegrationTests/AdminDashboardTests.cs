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
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.Requests;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
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
        Assert.Equal(30, data.DailySeries.Count);
        Assert.True(data.DailySeries[^1].Orders >= 1);
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
        Assert.Empty(data.RecentPayments);
        Assert.Empty(data.RecentUsers);
        Assert.Empty(data.DailySeries);
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
        Assert.NotEmpty(data.RecentUsers);
        Assert.Null(data.Orders);
        Assert.Null(data.Payments);
        Assert.Null(data.Inventory);
        Assert.Null(data.Catalog);
        Assert.Empty(data.RecentOrders);
        Assert.Empty(data.RecentPayments);
    }

    [Fact]
    public async Task GetDashboard_PaymentsRead_SeesRecentPaymentsButNoUsersSection()
    {
        await RegisterAndLoginAsync();
        var orderId = SeedPayment();
        try
        {
            AuthorizeAs(["Admin"], [PermissionCatalog.PaymentsRead]);

            var response = await _client.GetAsync("admin/dashboard");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
            Assert.NotNull(data.Payments);
            Assert.Contains(data.RecentPayments, item => item.OrderId == orderId);
            Assert.Null(data.Users);
            Assert.Empty(data.RecentUsers);
            Assert.Null(data.Orders);
            Assert.Empty(data.RecentOrders);
        }
        finally
        {
            DeletePayment(orderId);
        }
    }

    [Fact]
    public async Task GetDashboard_PaymentsStatsOnly_SeesStatsButNoRecentPayments()
    {
        await RegisterAndLoginAsync();
        var orderId = SeedPayment();
        try
        {
            AuthorizeAs(["Admin"], [PermissionCatalog.PaymentsStats]);

            var response = await _client.GetAsync("admin/dashboard");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;
            Assert.NotNull(data.Payments);
            Assert.Empty(data.RecentPayments);
            Assert.Null(data.Users);
            Assert.Empty(data.RecentUsers);
        }
        finally
        {
            DeletePayment(orderId);
        }
    }

    [Fact]
    public async Task GetDashboard_DailySeries_MergesOrdersAndRevenueAcrossDays()
    {
        var userId = await RegisterAndLoginAsync();
        var created = await CreateOrderForCurrentUserAsync("SKU-DASH-SERIES", userId);
        BackdateOrder(created.OrderId, DateTimeOffset.UtcNow.AddDays(-2));

        var paymentOrderId = SeedSucceededPayment(DateTimeOffset.UtcNow.AddDays(-1), 77.77m);
        try
        {
            AuthorizeAs(["Admin"], [PermissionCatalog.OrdersRead, PermissionCatalog.PaymentsRead]);

            var response = await _client.GetAsync("admin/dashboard?days=5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;

            Assert.Equal(5, data.DailySeries.Count);
            Assert.Equal(
                Enumerable.Range(0, 5).Select(index => DateTime.UtcNow.AddDays(index - 4).ToString("yyyy-MM-dd")).ToList(),
                data.DailySeries.Select(point => point.Date).ToList());

            var twoDaysAgo = data.DailySeries.Single(point => point.Date == DateDaysAgo(2));
            Assert.True(twoDaysAgo.Orders >= 1);

            var yesterday = data.DailySeries.Single(point => point.Date == DateDaysAgo(1));
            var revenue = Assert.Single(yesterday.Revenue);
            Assert.Equal("USD", revenue.Currency);
            Assert.Equal(77.77m, revenue.Amount);
        }
        finally
        {
            DeletePayment(paymentOrderId);
        }
    }

    [Fact]
    public async Task GetDashboard_DailySeries_StatsOnlyViewer_SeesRevenueWithOrdersZeroed()
    {
        await RegisterAndLoginAsync();
        var paymentOrderId = SeedSucceededPayment(DateTimeOffset.UtcNow.AddDays(-1), 33.33m);
        try
        {
            AuthorizeAs(["Admin"], [PermissionCatalog.PaymentsStats]);

            var response = await _client.GetAsync("admin/dashboard?days=5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.ReadJsonAsync<ApiResponseDto<AdminDashboardResponse>>()).Data!;

            Assert.Equal(5, data.DailySeries.Count);
            Assert.NotNull(data.Payments);
            Assert.All(data.DailySeries, point => Assert.Equal(0, point.Orders));

            var yesterday = data.DailySeries.Single(point => point.Date == DateDaysAgo(1));
            var revenue = Assert.Single(yesterday.Revenue);
            Assert.Equal("USD", revenue.Currency);
            Assert.Equal(33.33m, revenue.Amount);
        }
        finally
        {
            DeletePayment(paymentOrderId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task GetDashboard_InvalidDays_ReturnsBadRequest(int days)
    {
        await RegisterAndLoginAsync();

        var response = await _client.GetAsync($"admin/dashboard?days={days}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private Guid SeedPayment()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var orderId = Guid.NewGuid();
        dbContext.Set<Payment>().Add(new Payment(Guid.NewGuid(), orderId, 40.00m, "USD", Guid.NewGuid()));
        dbContext.SaveChanges();

        return orderId;
    }

    private Guid SeedSucceededPayment(DateTimeOffset createdAt, decimal amount)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), orderId, amount, "USD", Guid.NewGuid());
        payment.Succeed($"pi_{Guid.NewGuid():N}");
        dbContext.Set<Payment>().Add(payment);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();

        var tracked = dbContext.Set<Payment>().Single(item => item.Id == payment.Id);
        dbContext.Entry(tracked).Property(item => item.CreatedAtUtc).CurrentValue = createdAt;
        dbContext.SaveChanges();

        return orderId;
    }

    private void BackdateOrder(Guid orderId, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = dbContext.Set<Order>().Single(item => item.Id == new OrderId(orderId));
        dbContext.Entry(order).Property(item => item.CreatedAtUtc).CurrentValue = createdAt;
        dbContext.SaveChanges();
    }

    private static string DateDaysAgo(int days) =>
        DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");

    private void DeletePayment(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var payment = dbContext.Set<Payment>().SingleOrDefault(item => item.OrderId == orderId);
        if (payment is not null)
        {
            dbContext.Set<Payment>().Remove(payment);
            dbContext.SaveChanges();
        }
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
