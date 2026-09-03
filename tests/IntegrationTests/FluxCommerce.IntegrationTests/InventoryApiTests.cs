using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(InventoryApiTestCollection.Name)]
public class InventoryApiTests : InventoryApiTestBase
{
    public InventoryApiTests(InventoryWebApplicationFactory factory)
        : base(factory)
    {
    }

    // ---- Create ----

    [Fact]
    public async Task Create_WithValidSku_ReturnsCreatedAndPersistedState()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("CREATE");

        var response = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity = 25 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryItemResponse>>();
        Assert.True(payload.IsSuccess);
        Assert.Equal(sku.ToUpperInvariant(), payload.Data!.Sku);
        Assert.Equal(25, payload.Data.OnHandQuantity);
        Assert.Equal(0, payload.Data.ReservedQuantity);
        Assert.Equal(25, payload.Data.AvailableQuantity);
    }

    [Fact]
    public async Task Create_DuplicateSku_ReturnsConflict()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("DUP");

        var first = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity = 5 });
        first.EnsureSuccessStatusCode();

        var response = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity = 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("An inventory item with this SKU already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_InvalidSku_ReturnsBadRequest()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.PostAsJsonAsync("inventory", new { sku = "bad sku!", onHandQuantity = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The SKU is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_NegativeOnHandQuantity_ReturnsBadRequest()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.PostAsJsonAsync("inventory", new { sku = UniqueSku("NEG"), onHandQuantity = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ZeroOnHandQuantity_IsValid()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("ZEROINIT");

        var response = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity = 0 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryItemResponse>>();
        Assert.Equal(0, payload.Data!.OnHandQuantity);
        Assert.Equal(0, payload.Data.AvailableQuantity);
    }

    [Fact]
    public async Task Create_MissingSku_ReturnsBadRequest()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.PostAsJsonAsync("inventory", new { onHandQuantity = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Read ----

    [Fact]
    public async Task Get_ExistingSku_ReturnsStockState()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("GET");
        await CreateItemAsync(sku, 10);

        var response = await Client.GetAsync($"inventory/{sku}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryItemResponse>>();
        Assert.Equal(sku.ToUpperInvariant(), payload.Data!.Sku);
        Assert.Equal(10, payload.Data.OnHandQuantity);
        Assert.Equal(0, payload.Data.ReservedQuantity);
        Assert.Equal(10, payload.Data.AvailableQuantity);
    }

    [Fact]
    public async Task Get_MissingSku_ReturnsNotFound()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.GetAsync($"inventory/{UniqueSku("MISSING")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Get_InvalidSku_ReturnsBadRequest()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.GetAsync("inventory/BAD-SKU!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The SKU is invalid.", problem.Detail);
    }

    // ---- Adjust stock ----

    [Fact]
    public async Task AdjustStock_AddsQuantity_ReturnsUpdatedState()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("ADJUST");
        await CreateItemAsync(sku, 10);

        var response = await Client.PatchAsJsonAsync($"inventory/{sku}", new { quantity = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryItemResponse>>();
        Assert.Equal(17, payload.Data!.OnHandQuantity);
        Assert.Equal(0, payload.Data.ReservedQuantity);
        Assert.Equal(17, payload.Data.AvailableQuantity);
    }

    [Fact]
    public async Task AdjustStock_MissingSku_ReturnsNotFound()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.PatchAsJsonAsync($"inventory/{UniqueSku("NOADJ")}", new { quantity = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task AdjustStock_NonPositiveQuantity_ReturnsBadRequest()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("ZERO");
        await CreateItemAsync(sku, 10);

        var response = await Client.PatchAsJsonAsync($"inventory/{sku}", new { quantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Reservation lookup ----

    [Fact]
    public async Task GetReservation_Existing_ReturnsReservationState()
    {
        await AuthorizeAsAdminAsync();
        var sku = UniqueSku("RES");
        var reservationId = await Factory.SeedReservationAsync(sku, onHand: 10, quantity: 3, DateTimeOffset.UtcNow.AddMinutes(30));

        var response = await Client.GetAsync($"inventory/reservations/{reservationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.ReadJsonAsync<ApiResponseDto<InventoryReservationResponse>>();
        Assert.Equal(reservationId, payload.Data!.ReservationId);
        Assert.Equal(sku.ToUpperInvariant(), payload.Data.Sku);
        Assert.Equal(3, payload.Data.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, payload.Data.Status);
    }

    [Fact]
    public async Task GetReservation_Missing_ReturnsNotFound()
    {
        await AuthorizeAsAdminAsync();

        var response = await Client.GetAsync($"inventory/reservations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    private async Task CreateItemAsync(string sku, int onHandQuantity)
    {
        var response = await Client.PostAsJsonAsync("inventory", new { sku, onHandQuantity });
        response.EnsureSuccessStatusCode();
    }
}
