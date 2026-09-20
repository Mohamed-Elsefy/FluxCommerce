using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public sealed class PaymentInitializationTests
{
    private const string Password = "Str0ng!Pass1";

    private readonly IdentityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentInitializationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ByOrder_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync($"api/payments/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ByOrder_UnknownOrder_ReturnsMaskedNotFound()
    {
        await AuthorizeAsCustomerAsync();

        var response = await _client.GetAsync($"api/payments/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Payment not found.", problem.Detail);
    }

    [Fact]
    public async Task ByOrder_StrangersPayment_ReturnsMaskedNotFound()
    {
        var owner = await RegisterCustomerAsync();
        var strangerOrderId = Guid.NewGuid();
        SeedPayment(strangerOrderId, owner.UserId, intentId: $"pi_stranger_{Guid.NewGuid():N}");

        await AuthorizeAsCustomerAsync();

        var response = await _client.GetAsync($"api/payments/by-order/{strangerOrderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Payment not found.", problem.Detail);
    }

    [Fact]
    public async Task ByOrder_PaymentWithoutIntent_ReturnsNotReady()
    {
        var owner = await RegisterCustomerAsync();
        var orderId = Guid.NewGuid();
        SeedPayment(orderId, owner.UserId, intentId: null);
        await LoginAsync(owner.Email);

        var response = await _client.GetAsync($"api/payments/by-order/{orderId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Payment is not ready yet; retry shortly.", problem.Detail);
    }

    private async Task<(string Email, Guid UserId)> RegisterCustomerAsync()
    {
        var email = $"pay-init-{Guid.NewGuid():N}@example.com";

        var register = await _client.PostAsJsonAsync(
            "auth/register",
            new { email, password = Password, firstName = "Pay", lastName = "Init" });
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

    private async Task AuthorizeAsCustomerAsync()
    {
        var (email, _) = await RegisterCustomerAsync();
        await LoginAsync(email);
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
