using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the customer-facing payment list: <c>GET /api/payments/mine</c> returns only the
/// caller's payments with an optional status filter. Payments are seeded directly into the
/// module database following the <see cref="PaymentInitializationTests"/> pattern.
/// </summary>
[Collection(IdentityTestCollection.Name)]
public sealed class MyPaymentsTests
{
    private const string Password = "Str0ng!Pass1";

    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MyPaymentsTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Mine_ReturnsOnlyCallerPayments_WithStatusFilter()
    {
        var owner = await RegisterCustomerAsync();
        var other = await RegisterCustomerAsync();
        var ownOrderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        SeedPayment(ownOrderId, owner.UserId, $"pi_mine_{Guid.NewGuid():N}");
        SeedPayment(otherOrderId, other.UserId, $"pi_other_{Guid.NewGuid():N}");
        await LoginAsync(owner.Email);

        var response = await _client.GetAsync("api/payments/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.ReadJsonAsync<ApiResponseDto<PagedResponseDto<PaymentAdminResponse>>>()).Data!;
        Assert.Contains(page.Items, item => item.OrderId == ownOrderId);
        Assert.DoesNotContain(page.Items, item => item.OrderId == otherOrderId);

        var pending = await _client.GetAsync("api/payments/mine?status=Pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingPage = (await pending.ReadJsonAsync<ApiResponseDto<PagedResponseDto<PaymentAdminResponse>>>()).Data!;
        Assert.Contains(pendingPage.Items, item => item.OrderId == ownOrderId);

        var succeeded = await _client.GetAsync("api/payments/mine?status=Succeeded");
        Assert.Equal(HttpStatusCode.OK, succeeded.StatusCode);
        var succeededPage = (await succeeded.ReadJsonAsync<ApiResponseDto<PagedResponseDto<PaymentAdminResponse>>>()).Data!;
        Assert.DoesNotContain(succeededPage.Items, item => item.OrderId == ownOrderId);

        var invalid = await _client.GetAsync("api/payments/mine?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Mine_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("api/payments/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(string Email, Guid UserId)> RegisterCustomerAsync()
    {
        var email = $"pay-mine-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = Password, firstName = "Pay", lastName = "Mine" });
        register.EnsureSuccessStatusCode();

        var verificationToken = TestDatabase.GetVerificationToken(_factory, email);

        var verify = await _client.PostAsJsonAsync("auth/verify-email", new { token = verificationToken });
        verify.EnsureSuccessStatusCode();

        return (email, TestDatabase.GetUserId(_factory, email));
    }

    private async Task LoginAsync(string email)
    {
        var login = await _client.PostAsJsonAsync("auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var accessToken = (await login.ReadJsonAsync<ApiResponseDto<LoginResponse>>()).Data!.AccessToken;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private void SeedPayment(Guid orderId, Guid customerId, string? intentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        dbContext.Database.EnsureCreated();

        var payment = new Payment(Guid.NewGuid(), orderId, 42.50m, "USD", customerId);
        if (intentId is not null)
            payment.UpdateProviderReference(intentId, "Stripe");

        dbContext.Set<Payment>().Add(payment);
        dbContext.SaveChanges();
    }
}
