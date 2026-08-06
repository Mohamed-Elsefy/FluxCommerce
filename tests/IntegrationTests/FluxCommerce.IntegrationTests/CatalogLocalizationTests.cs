using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class CatalogLocalizationTests : CatalogApiTestBase
{
    public CatalogLocalizationTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task DuplicateCategoryName_InFrench_ReturnsFrenchDetailAndTitle()
    {
        Client.DefaultRequestHeaders.Add("Accept-Language", "fr");
        await AuthorizeAsync();
        await CreateCategoryAsync("Catégorie Existante", "categorie-existante");

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Catégorie Existante", slug = "autre-slug", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Une catégorie avec ce nom existe déjà.", problem.Detail);
        Assert.Equal("Conflit", problem.Title);
    }

    [Fact]
    public async Task InvalidSlug_InFrench_ReturnsFrenchDetailAndTitle()
    {
        Client.DefaultRequestHeaders.Add("Accept-Language", "fr");
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Slug Invalide", slug = "slug invalide!", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("Le slug est invalide.", problem.Detail);
        Assert.Equal("Échec de la validation", problem.Title);
    }

    [Fact]
    public async Task NotFound_InFrench_ReturnsFrenchDetailAndTitle()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr");

        var response = await client.GetAsync($"catalog/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("La ressource demandée est introuvable.", problem.Detail);
        Assert.Equal("Introuvable", problem.Title);
    }

    [Fact]
    public async Task NotFound_InEnglish_ReturnsEnglishDetailAndTitle()
    {
        var response = await Client.GetAsync($"catalog/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
        Assert.Equal("Not Found", problem.Title);
    }

    [Fact]
    public async Task ValidationFailure_DefaultCulture_ReturnsEnglishDetail()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = string.Empty, slug = "empty-name", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }
}
