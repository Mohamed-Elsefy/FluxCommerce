using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Queries.ListAttributeDefinitions;
using FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductAttributeValues;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductImages;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductSpecifications;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductVariants;
using FluxCommerce.Modules.Catalog.Application.Queries.SearchCatalogProducts;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ListAndSearchQueryHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeVariantRepository _variants = new();
    private readonly FakeAttributeDefinitionRepository _attributeDefinitions = new();

    // ---- ListCategories ----

    [Fact]
    public async Task ListCategories_ReturnsAllCategories()
    {
        await _categories.AddAsync(TestCategories.Create(name: "Electronics", slug: "electronics"));
        await _categories.AddAsync(TestCategories.Create(name: "Books", slug: "books"));
        var handler = new ListCategoriesQueryHandler(_categories);

        var result = await handler.HandleAsync(new ListCategoriesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, category => category.Name == "Electronics");
        Assert.Contains(result.Value, category => category.Name == "Books");
    }

    [Fact]
    public async Task ListCategories_Empty_ReturnsEmptyCollection()
    {
        var handler = new ListCategoriesQueryHandler(_categories);

        var result = await handler.HandleAsync(new ListCategoriesQuery());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ---- ListProducts ----

    [Fact]
    public async Task ListProducts_ReturnsAllProducts()
    {
        await _products.AddAsync(TestProducts.Create(name: "Phone", slug: "phone"));
        await _products.AddAsync(TestProducts.Create(name: "Tablet", slug: "tablet"));
        var handler = new ListProductsQueryHandler(_products);

        var result = await handler.HandleAsync(new ListProductsQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    // ---- ListAttributeDefinitions ----

    [Fact]
    public async Task ListAttributeDefinitions_ReturnsAllDefinitions()
    {
        await _attributeDefinitions.AddAsync(TestAttributeDefinitions.Create(name: "RAM", slug: "ram"));
        var handler = new ListAttributeDefinitionsQueryHandler(_attributeDefinitions);

        var result = await handler.HandleAsync(new ListAttributeDefinitionsQuery());

        Assert.True(result.IsSuccess);
        var attributeDefinition = Assert.Single(result.Value);
        Assert.Equal("RAM", attributeDefinition.Name);
    }

    // ---- ListProductVariants ----

    [Fact]
    public async Task ListVariants_ReturnsOnlyVariantsForProduct()
    {
        var productId = Guid.NewGuid();
        await _variants.AddAsync(TestVariants.Create(sku: "SKU-1", productId: productId));
        await _variants.AddAsync(TestVariants.Create(sku: "SKU-2", productId: productId));
        await _variants.AddAsync(TestVariants.Create(sku: "SKU-OTHER"));
        var handler = new ListProductVariantsQueryHandler(_variants);

        var result = await handler.HandleAsync(new ListProductVariantsQuery(productId));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, variant => Assert.Equal(productId, variant.ProductId));
    }

    // ---- ListProductImages ----

    [Fact]
    public async Task ListImages_ReturnsProductImages()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, true, 0, null, TestProducts.UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, TestProducts.UtcNow);
        await _products.AddAsync(product);
        var handler = new ListProductImagesQueryHandler(_products);

        var result = await handler.HandleAsync(new ListProductImagesQuery(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, image => Assert.Equal(product.Id, image.ProductId));
    }

    [Fact]
    public async Task ListImages_ProductNotFound_ReturnsNotFound()
    {
        var handler = new ListProductImagesQueryHandler(_products);

        var result = await handler.HandleAsync(new ListProductImagesQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- ListProductSpecifications ----

    [Fact]
    public async Task ListSpecifications_ReturnsProductSpecifications()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", TestProducts.UtcNow);
        product.AddSpecification("Weight", "200g", TestProducts.UtcNow);
        await _products.AddAsync(product);
        var handler = new ListProductSpecificationsQueryHandler(_products);

        var result = await handler.HandleAsync(new ListProductSpecificationsQuery(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, specification => Assert.Equal(product.Id, specification.ProductId));
    }

    [Fact]
    public async Task ListSpecifications_ProductNotFound_ReturnsNotFound()
    {
        var handler = new ListProductSpecificationsQueryHandler(_products);

        var result = await handler.HandleAsync(new ListProductSpecificationsQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    // ---- ListProductAttributeValues ----

    [Fact]
    public async Task ListAttributeValues_ReturnsMappedValues()
    {
        var product = TestProducts.Create();
        var ram = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        var storage = TestAttributeDefinitions.Create(name: "Storage", slug: "storage");
        product.AssignAttributeValue(ram, "8GB", TestProducts.UtcNow);
        product.AssignAttributeValue(storage, "256GB", TestProducts.UtcNow);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(ram);
        await _attributeDefinitions.AddAsync(storage);
        var handler = new ListProductAttributeValuesQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new ListProductAttributeValuesQuery(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, value => value.AttributeName == "RAM" && value.Value == "8GB");
        Assert.Contains(result.Value, value => value.AttributeName == "Storage" && value.Value == "256GB");
    }

    [Fact]
    public async Task ListAttributeValues_MissingDefinition_SkipsValue()
    {
        var product = TestProducts.Create();
        var ram = TestAttributeDefinitions.Create();
        product.AssignAttributeValue(ram, "8GB", TestProducts.UtcNow);
        await _products.AddAsync(product);
        var handler = new ListProductAttributeValuesQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new ListProductAttributeValuesQuery(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ListAttributeValues_ProductNotFound_ReturnsNotFound()
    {
        var handler = new ListProductAttributeValuesQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new ListProductAttributeValuesQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    // ---- SearchCatalogProducts ----

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmptyPage()
    {
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(""));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(20, result.Value.PageSize);
    }

    [Fact]
    public async Task Search_MatchesName_ReturnsItems()
    {
        var product = TestProducts.Create(name: "Smartphone", slug: "smartphone");
        await _products.AddAsync(product);
        await _products.AddAsync(TestProducts.Create(name: "Laptop", slug: "laptop"));
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery("smart"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(product.Id, result.Value.Items.ElementAt(0).Id);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Search_FilterByStatus_ReturnsFilteredItems()
    {
        var active = TestProducts.Create(name: "Phone", slug: "phone");
        active.Activate();
        var draft = TestProducts.Create(name: "Draft", slug: "draft");
        await _products.AddAsync(active);
        await _products.AddAsync(draft);
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(
            "",
            new CatalogProductFilter(Status: ProductStatus.Active)));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(active.Id, item.Id);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Search_FilterByCategory_ReturnsFilteredItems()
    {
        var category = TestCategories.Create();
        var product = TestProducts.Create(name: "Phone", slug: "phone", categoryId: category.Id);
        await _products.AddAsync(product);
        await _products.AddAsync(TestProducts.Create(name: "Other", slug: "other"));
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(
            "",
            new CatalogProductFilter(CategoryId: category.Id)));

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(product.Id, item.Id);
    }

    [Fact]
    public async Task Search_SortsByNameAscending_ByDefault()
    {
        await _products.AddAsync(TestProducts.Create(name: "Zebra", slug: "zebra"));
        await _products.AddAsync(TestProducts.Create(name: "Apple", slug: "apple"));
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(
            "",
            new CatalogProductFilter(IsDeleted: false),
            SortBy: CatalogProductSortBy.Name,
            SortDirection: CatalogProductSortDirection.Ascending));

        Assert.True(result.IsSuccess);
        Assert.Equal("Apple", result.Value.Items.ElementAt(0).Name);
        Assert.Equal("Zebra", result.Value.Items.ElementAt(1).Name);
    }

    [Fact]
    public async Task Search_SortsByNameDescending()
    {
        await _products.AddAsync(TestProducts.Create(name: "Zebra", slug: "zebra"));
        await _products.AddAsync(TestProducts.Create(name: "Apple", slug: "apple"));
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(
            "",
            new CatalogProductFilter(IsDeleted: false),
            SortBy: CatalogProductSortBy.Name,
            SortDirection: CatalogProductSortDirection.Descending));

        Assert.True(result.IsSuccess);
        Assert.Equal("Zebra", result.Value.Items.ElementAt(0).Name);
        Assert.Equal("Apple", result.Value.Items.ElementAt(1).Name);
    }

    [Fact]
    public async Task Search_Paginates_ReturnsPageAndTotalCount()
    {
        for (var i = 1; i <= 5; i++)
            await _products.AddAsync(TestProducts.Create(name: $"Product {i}", slug: $"product-{i}"));

        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery(
            "",
            new CatalogProductFilter(IsDeleted: false),
            PageNumber: 2,
            PageSize: 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(2, result.Value.PageSize);
    }

    [Fact]
    public async Task Search_InvalidPageNumber_ReturnsValidation()
    {
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery("", PageNumber: 0));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Search_InvalidPageSize_ReturnsValidation()
    {
        var handler = new SearchCatalogProductsQueryHandler(_products, new FakeLocalizer());

        var result = await handler.HandleAsync(new SearchCatalogProductsQuery("", PageSize: 101));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }
}
