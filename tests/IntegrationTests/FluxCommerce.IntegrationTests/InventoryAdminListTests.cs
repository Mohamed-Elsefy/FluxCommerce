using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers the administrative Inventory list and counts surface: item list with search and
/// available-quantity filters, reservation list with status/SKU filters, aggregate counts
/// and Admin-only authorization. The shared collection database is used, so assertions are
/// containment-based with test-unique SKUs.
/// </summary>
[Collection(InventoryApiTestCollection.Name)]
public class InventoryAdminListTests : InventoryApiTestBase
{
    public InventoryAdminListTests(InventoryWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task List_ReturnsCreatedItems_AndAppliesSearchFilter()
    {
        await AuthorizeAsAdminAsync();
        var prefix = $"LST-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var skuA = UniqueSku(prefix + "A");
        var skuB = UniqueSku(prefix + "B");

        var createA = await Client.PostAsJsonAsync("inventory", new { sku = skuA, onHandQuantity = 100 });
        createA.EnsureSuccessStatusCode();
        var createB = await Client.PostAsJsonAsync("inventory", new { sku = skuB, onHandQuantity = 100 });
        createB.EnsureSuccessStatusCode();

        var unfiltered = await Client.GetAsync("inventory?pageNumber=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
        var page = await unfiltered.ReadJsonAsync<ApiResponseDto<PagedResponseDto<InventoryItemResponse>>>();
        Assert.True(page.IsSuccess);
        Assert.Contains(page.Data!.Items, item => item.Sku == skuA.ToUpperInvariant());
        Assert.Contains(page.Data.Items, item => item.Sku == skuB.ToUpperInvariant());

        var filtered = await Client.GetAsync($"inventory?search={skuA}");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var filteredPage = await filtered.ReadJsonAsync<ApiResponseDto<PagedResponseDto<InventoryItemResponse>>>();
        Assert.True(filteredPage.IsSuccess);
        Assert.Contains(filteredPage.Data!.Items, item => item.Sku == skuA.ToUpperInvariant());
        Assert.DoesNotContain(filteredPage.Data.Items, item => item.Sku == skuB.ToUpperInvariant());
    }

    [Fact]
    public async Task List_AppliesMaxAvailableFilter()
    {
        await AuthorizeAsAdminAsync();
        var prefix = $"MAX-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var stockedSku = UniqueSku(prefix + "S");
        var lowSku = UniqueSku(prefix + "L");

        var createStocked = await Client.PostAsJsonAsync("inventory", new { sku = stockedSku, onHandQuantity = 100 });
        createStocked.EnsureSuccessStatusCode();
        var createLow = await Client.PostAsJsonAsync("inventory", new { sku = lowSku, onHandQuantity = 2 });
        createLow.EnsureSuccessStatusCode();

        var response = await Client.GetAsync("inventory?maxAvailable=5&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadJsonAsync<ApiResponseDto<PagedResponseDto<InventoryItemResponse>>>();
        Assert.True(page.IsSuccess);
        Assert.Contains(page.Data!.Items, item => item.Sku == lowSku.ToUpperInvariant());
        Assert.DoesNotContain(page.Data.Items, item => item.Sku == stockedSku.ToUpperInvariant());
    }

    [Fact]
    public async Task ListReservations_FiltersBySkuAndStatus()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("RESF");
        var reservationId = await Factory.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 2,
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));

        var bySku = await Client.GetAsync($"inventory/reservations?sku={sku}");
        Assert.Equal(HttpStatusCode.OK, bySku.StatusCode);
        var page = await bySku.ReadJsonAsync<ApiResponseDto<PagedResponseDto<InventoryReservationResponse>>>();
        Assert.True(page.IsSuccess);
        Assert.Contains(page.Data!.Items, item => item.ReservationId == reservationId);

        var byStatus = await Client.GetAsync($"inventory/reservations?sku={sku}&status=Expired");
        Assert.Equal(HttpStatusCode.OK, byStatus.StatusCode);
        var statusPage = await byStatus.ReadJsonAsync<ApiResponseDto<PagedResponseDto<InventoryReservationResponse>>>();
        Assert.True(statusPage.IsSuccess);
        Assert.DoesNotContain(statusPage.Data!.Items, item => item.ReservationId == reservationId);
    }

    [Fact]
    public async Task Counts_ReflectsSeededState()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("CNT");
        await Factory.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 2,
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));

        var response = await Client.GetAsync("inventory/counts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryCountsResponse>>();
        Assert.True(payload.IsSuccess);
        Assert.True(payload.Data!.TotalItems >= 1);
        Assert.True(payload.Data.TotalOnHand >= 10);
        Assert.True(payload.Data.TotalReserved >= 2);
        Assert.True(payload.Data.PendingReservations >= 1);
    }

    [Fact]
    public async Task List_RequiresAdmin_ReturnsForbiddenForCustomer()
    {
        await AuthorizeAsync();

        var response = await Client.GetAsync("inventory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
