using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Queries.GetAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCategory;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProduct;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductImage;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductVariant;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;
using ContractEnums = FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.UnitTests.Application;

public class GetQueryHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeCategoryRepository _categories = new();
    private readonly FakeVariantRepository _variants = new();
    private readonly FakeAttributeDefinitionRepository _attributeDefinitions = new();

    [Fact]
    public async Task GetProduct_Succeeds_AndMapsResponse()
    {
        var product = TestProducts.Create(name: "Smartphone", slug: "smartphone");
        await _products.AddAsync(product);
        var handler = new GetProductQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductQuery(product.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(product.Id, result.Value.Id);
        Assert.Equal("Smartphone", result.Value.Name);
        Assert.Equal("smartphone", result.Value.Slug);
        Assert.Equal(ContractEnums.ProductStatus.Draft, result.Value.Status);
        Assert.Equal(ContractEnums.ProductVisibility.Visible, result.Value.Visibility);
        Assert.Null(result.Value.Seo);
    }

    [Fact]
    public async Task GetProduct_NotFound_ReturnsNotFound()
    {
        var handler = new GetProductQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetCategory_Succeeds_AndMapsResponse()
    {
        var category = TestCategories.Create(name: "Electronics", slug: "electronics");
        await _categories.AddAsync(category);
        var handler = new GetCategoryQueryHandler(_categories);

        var result = await handler.HandleAsync(new GetCategoryQuery(category.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(category.Id, result.Value.Id);
        Assert.Equal("Electronics", result.Value.Name);
        Assert.Equal("electronics", result.Value.Slug);
        Assert.Equal(ContractEnums.CategoryStatus.Active, result.Value.Status);
        Assert.Null(result.Value.ParentId);
    }

    [Fact]
    public async Task GetCategory_NotFound_ReturnsNotFound()
    {
        var handler = new GetCategoryQueryHandler(_categories);

        var result = await handler.HandleAsync(new GetCategoryQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CategoryNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetAttributeDefinition_Succeeds_AndMapsResponse()
    {
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new GetAttributeDefinitionQueryHandler(_attributeDefinitions);

        var result = await handler.HandleAsync(new GetAttributeDefinitionQuery(attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(attributeDefinition.Id, result.Value.Id);
        Assert.Equal("RAM", result.Value.Name);
        Assert.Equal("ram", result.Value.Slug);
        Assert.Equal(ContractEnums.AttributeStatus.Active, result.Value.Status);
    }

    [Fact]
    public async Task GetAttributeDefinition_NotFound_ReturnsNotFound()
    {
        var handler = new GetAttributeDefinitionQueryHandler(_attributeDefinitions);

        var result = await handler.HandleAsync(new GetAttributeDefinitionQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetVariant_Succeeds_AndMapsResponse()
    {
        var variant = TestVariants.Create(sku: "SKU-1", name: "128GB");
        await _variants.AddAsync(variant);
        var handler = new GetProductVariantQueryHandler(_variants);

        var result = await handler.HandleAsync(new GetProductVariantQuery(variant.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(variant.Id, result.Value.Id);
        Assert.Equal("SKU-1", result.Value.Sku);
        Assert.Equal("128GB", result.Value.Name);
        Assert.Equal(ContractEnums.VariantStatus.Active, result.Value.Status);
    }

    [Fact]
    public async Task GetVariant_NotFound_ReturnsNotFound()
    {
        var handler = new GetProductVariantQueryHandler(_variants);

        var result = await handler.HandleAsync(new GetProductVariantQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.VariantNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetImage_Succeeds_AndMapsResponse()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", "Alt", true, 0, new Dictionary<string, string> { ["k"] = "v" }, TestProducts.UtcNow);
        var image = product.Images.Single();
        await _products.AddAsync(product);
        var handler = new GetProductImageQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductImageQuery(product.Id, image.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(image.Id, result.Value.Id);
        Assert.Equal(product.Id, result.Value.ProductId);
        Assert.Equal("https://example.com/a.jpg", result.Value.ImageUrl);
        Assert.True(result.Value.IsPrimary);
        Assert.Equal("v", result.Value.Metadata!["k"]);
    }

    [Fact]
    public async Task GetImage_ProductNotFound_ReturnsNotFound()
    {
        var handler = new GetProductImageQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductImageQuery(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetImage_ImageNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new GetProductImageQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductImageQuery(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductImageNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetSpecification_Succeeds_AndMapsResponse()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", TestProducts.UtcNow);
        var specification = product.Specifications.Single();
        await _products.AddAsync(product);
        var handler = new GetProductSpecificationQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductSpecificationQuery(product.Id, specification.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(specification.Id, result.Value.Id);
        Assert.Equal(product.Id, result.Value.ProductId);
        Assert.Equal("Color", result.Value.Name);
        Assert.Equal("Black", result.Value.Value);
    }

    [Fact]
    public async Task GetSpecification_ProductNotFound_ReturnsNotFound()
    {
        var handler = new GetProductSpecificationQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductSpecificationQuery(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetSpecification_SpecificationNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new GetProductSpecificationQueryHandler(_products);

        var result = await handler.HandleAsync(new GetProductSpecificationQuery(product.Id, Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductSpecificationNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetAttributeValue_Succeeds_AndMapsResponse()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        product.AssignAttributeValue(attributeDefinition, "8GB", TestProducts.UtcNow);
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new GetProductAttributeValueQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new GetProductAttributeValueQuery(product.Id, attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(attributeDefinition.Id, result.Value.AttributeDefinitionId);
        Assert.Equal("RAM", result.Value.AttributeName);
        Assert.Equal("8GB", result.Value.Value);
    }

    [Fact]
    public async Task GetAttributeValue_ProductNotFound_ReturnsNotFound()
    {
        var handler = new GetProductAttributeValueQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new GetProductAttributeValueQuery(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetAttributeValue_ValueNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _products.AddAsync(product);
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new GetProductAttributeValueQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new GetProductAttributeValueQuery(product.Id, attributeDefinition.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductAttributeValueNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetAttributeValue_AttributeDefinitionNotFound_ReturnsNotFound()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create();
        product.AssignAttributeValue(attributeDefinition, "8GB", TestProducts.UtcNow);
        await _products.AddAsync(product);
        var handler = new GetProductAttributeValueQueryHandler(_products, _attributeDefinitions);

        var result = await handler.HandleAsync(new GetProductAttributeValueQuery(product.Id, attributeDefinition.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }
}
