using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(InventoryApiTestCollection.Name)]
public class InventoryAuthorizationTests : InventoryApiTestBase
{
    public InventoryAuthorizationTests(InventoryWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Theory]
    [InlineData("inventory")]
    public async Task ManagementEndpoints_WithoutToken_ReturnUnauthorized(string endpoint)
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(endpoint, new { sku = UniqueSku("UNAUTH"), onHandQuantity = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("inventory/SKU-READ")]
    [InlineData("inventory/reservations/00000000-0000-0000-0000-000000000000")]
    public async Task ReadEndpoints_WithoutToken_ReturnUnauthorized(string endpoint)
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("inventory")]
    public async Task ManagementEndpoints_WithoutToken_PatchReturnsUnauthorized(string endpoint)
    {
        var client = Factory.CreateClient();

        var response = await client.PatchAsJsonAsync($"{endpoint}/SKU-ADJUST", new { quantity = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotManageInventory_ReturnsForbidden()
    {
        await AuthorizeAsync();

        var create = await Client.PostAsJsonAsync("inventory", new { sku = UniqueSku("FORBID"), onHandQuantity = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var patch = await Client.PatchAsJsonAsync($"inventory/{UniqueSku("FORBID")}", new { quantity = 3 });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotReadInventory_ReturnsForbidden()
    {
        await AuthorizeAsync();

        var get = await Client.GetAsync($"inventory/{UniqueSku("FORBID")}");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var reservation = await Client.GetAsync($"inventory/reservations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, reservation.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotReadAnotherPartysReservation_ReturnsForbidden()
    {
        // An admin creates the item and its reservation, then a plain customer attempts to look it
        // up by id. The Admin role gate prevents the customer from enumerating/reading arbitrary
        // reservation resources (IDOR is closed at the authorization boundary).
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("IDOR");
        var reservationId = await Factory.SeedReservationAsync(sku, onHand: 10, quantity: 3, DateTimeOffset.UtcNow.AddMinutes(30));

        var adminRead = await Client.GetAsync($"inventory/reservations/{reservationId}");
        Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);

        await AuthorizeAsync();

        var customerRead = await Client.GetAsync($"inventory/reservations/{reservationId}");
        Assert.Equal(HttpStatusCode.Forbidden, customerRead.StatusCode);
    }

    [Fact]
    public async Task Admin_CanManageAndReadInventory()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("ADMIN");

        var create = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity = 10 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var get = await Client.GetAsync($"inventory/{sku}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var patch = await Client.PatchAsJsonAsync($"inventory/{sku}", new { quantity = 2 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    }
}
