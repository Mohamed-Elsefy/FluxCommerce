using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;
using ContractEnums = FluxCommerce.Modules.Catalog.Contracts.Enums;

namespace FluxCommerce.UnitTests.Application;

public class MapperTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    // ---- CategoryResponseMapper ----

    [Fact]
    public void CategoryMapper_MapsAllFields()
    {
        var category = TestCategories.Create(name: "Electronics", slug: "electronics", description: "Desc");
        category.Deactivate();

        var response = CategoryResponseMapper.ToResponse(category);

        Assert.Equal(category.Id, response.Id);
        Assert.Equal("Electronics", response.Name);
        Assert.Equal("electronics", response.Slug);
        Assert.Equal("Desc", response.Description);
        Assert.Equal(ContractEnums.CategoryStatus.Inactive, response.Status);
        Assert.False(response.IsDeleted);
        Assert.Equal(category.CreatedAtUtc, response.CreatedAtUtc);
        Assert.Null(response.DeletedAtUtc);
    }

    [Fact]
    public void CategoryMapper_ToResponses_MapsCollection()
    {
        var categories = new[] { TestCategories.Create(), TestCategories.Create(name: "Books", slug: "books") };

        var responses = CategoryResponseMapper.ToResponses(categories);

        Assert.Equal(2, responses.Count);
    }

    // ---- ProductResponseMapper ----

    [Fact]
    public void ProductMapper_MapsAllFields_WithoutSeo()
    {
        var product = TestProducts.Create(name: "Phone", slug: "phone", description: "Desc");

        var response = ProductResponseMapper.ToResponse(product);

        Assert.Equal(product.Id, response.Id);
        Assert.Equal("Phone", response.Name);
        Assert.Equal("phone", response.Slug);
        Assert.Equal("Desc", response.Description);
        Assert.Equal(ContractEnums.ProductStatus.Draft, response.Status);
        Assert.Equal(ContractEnums.ProductVisibility.Visible, response.Visibility);
        Assert.Null(response.Seo);
        Assert.False(response.IsDeleted);
    }

    [Fact]
    public void ProductMapper_MapsSeoMetadata()
    {
        var product = TestProducts.Create(seo: SeoMetadata.Create("Title", "Desc", new[] { "k1", "k2" }));

        var response = ProductResponseMapper.ToResponse(product);

        Assert.NotNull(response.Seo);
        Assert.Equal("Title", response.Seo.Title);
        Assert.Equal("Desc", response.Seo.Description);
        Assert.Equal(new[] { "k1", "k2" }, response.Seo.Keywords);
    }

    [Fact]
    public void ProductMapper_MapsActivatedStatus()
    {
        var product = TestProducts.Create();
        product.Activate();

        var response = ProductResponseMapper.ToResponse(product);

        Assert.Equal(ContractEnums.ProductStatus.Active, response.Status);
    }

    [Fact]
    public void ProductMapper_ToResponses_MapsCollection()
    {
        var products = new[] { TestProducts.Create(), TestProducts.Create(name: "Laptop", slug: "laptop") };

        var responses = ProductResponseMapper.ToResponses(products);

        Assert.Equal(2, responses.Count);
    }

    // ---- VariantResponseMapper ----

    [Fact]
    public void VariantMapper_MapsAllFields()
    {
        var variant = TestVariants.Create(sku: "SKU-1", name: "128GB");

        var response = VariantResponseMapper.ToResponse(variant);

        Assert.Equal(variant.Id, response.Id);
        Assert.Equal(variant.ProductId, response.ProductId);
        Assert.Equal("SKU-1", response.Sku);
        Assert.Equal("128GB", response.Name);
        Assert.Equal(variant.UnitPrice.Amount, response.UnitPriceAmount);
        Assert.Equal(variant.UnitPrice.Currency, response.Currency);
        Assert.Equal(ContractEnums.VariantStatus.Active, response.Status);
        Assert.False(response.IsDeleted);
    }

    [Fact]
    public void VariantMapper_ToResponses_MapsCollection()
    {
        var variants = new[] { TestVariants.Create(sku: "SKU-1"), TestVariants.Create(sku: "SKU-2") };

        var responses = VariantResponseMapper.ToResponses(variants);

        Assert.Equal(2, responses.Count);
    }

    // ---- AttributeDefinitionResponseMapper ----

    [Fact]
    public void AttributeDefinitionMapper_MapsAllFields()
    {
        var attributeDefinition = TestAttributeDefinitions.Create(
            name: "RAM",
            slug: "ram",
            dataType: AttributeDataType.Number,
            isRequired: true,
            isFilterable: true,
            isSearchable: true,
            sortOrder: 5,
            categoryId: Guid.NewGuid());

        var response = AttributeDefinitionResponseMapper.ToResponse(attributeDefinition);

        Assert.Equal(attributeDefinition.Id, response.Id);
        Assert.Equal("RAM", response.Name);
        Assert.Equal("ram", response.Slug);
        Assert.Equal(ContractEnums.AttributeDataType.Number, response.DataType);
        Assert.True(response.IsRequired);
        Assert.True(response.IsFilterable);
        Assert.True(response.IsSearchable);
        Assert.Equal(5, response.SortOrder);
        Assert.NotNull(response.CategoryId);
        Assert.Equal(ContractEnums.AttributeStatus.Active, response.Status);
    }

    [Fact]
    public void AttributeDefinitionMapper_MapsAllDataTypes()
    {
        foreach (var dataType in Enum.GetValues<AttributeDataType>())
        {
            var attributeDefinition = TestAttributeDefinitions.Create(dataType: dataType);

            var response = AttributeDefinitionResponseMapper.ToResponse(attributeDefinition);

            Assert.Equal((ContractEnums.AttributeDataType)(int)dataType, response.DataType);
        }
    }

    [Fact]
    public void AttributeDefinitionMapper_ToResponses_MapsCollection()
    {
        var definitions = new[] { TestAttributeDefinitions.Create(), TestAttributeDefinitions.Create(name: "Storage", slug: "storage") };

        var responses = AttributeDefinitionResponseMapper.ToResponses(definitions);

        Assert.Equal(2, responses.Count);
    }

    // ---- ProductImageResponseMapper ----

    [Fact]
    public void ImageMapper_MapsAllFields()
    {
        var productId = Guid.NewGuid();
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", "Alt", true, 2, new Dictionary<string, string> { ["k"] = "v" }, UtcNow);
        var image = product.Images.Single();

        var response = ProductImageResponseMapper.ToResponse(image, productId);

        Assert.Equal(image.Id, response.Id);
        Assert.Equal(productId, response.ProductId);
        Assert.Equal("https://example.com/a.jpg", response.ImageUrl);
        Assert.Equal("Alt", response.AltText);
        Assert.Equal(2, response.DisplayOrder);
        Assert.True(response.IsPrimary);
        Assert.Equal("v", response.Metadata!["k"]);
        Assert.Equal(image.CreatedAtUtc, response.CreatedAtUtc);
    }

    [Fact]
    public void ImageMapper_ToResponses_OrdersByDisplayOrder()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 1, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 0, null, UtcNow);

        var responses = ProductImageResponseMapper.ToResponses(product.Id, product.Images);

        Assert.Equal("https://example.com/b.jpg", responses.ElementAt(0).ImageUrl);
        Assert.Equal("https://example.com/a.jpg", responses.ElementAt(1).ImageUrl);
    }

    // ---- ProductSpecificationResponseMapper ----

    [Fact]
    public void SpecificationMapper_MapsAllFields()
    {
        var productId = Guid.NewGuid();
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", UtcNow);
        var specification = product.Specifications.Single();

        var response = ProductSpecificationResponseMapper.ToResponse(specification, productId);

        Assert.Equal(specification.Id, response.Id);
        Assert.Equal(productId, response.ProductId);
        Assert.Equal("Color", response.Name);
        Assert.Equal("Black", response.Value);
    }

    [Fact]
    public void SpecificationMapper_ToResponses_MapsCollection()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", UtcNow);
        product.AddSpecification("Weight", "200g", UtcNow);

        var responses = ProductSpecificationResponseMapper.ToResponses(product.Id, product.Specifications);

        Assert.Equal(2, responses.Count);
        Assert.All(responses, response => Assert.Equal(product.Id, response.ProductId));
    }

    // ---- ProductAttributeValueResponseMapper ----

    [Fact]
    public void AttributeValueMapper_MapsAllFields()
    {
        var product = TestProducts.Create();
        var attributeDefinition = TestAttributeDefinitions.Create(name: "RAM", slug: "ram", dataType: AttributeDataType.Number);
        product.AssignAttributeValue(attributeDefinition, "8", UtcNow);
        var attributeValue = product.AttributeValues.Single();

        var response = ProductAttributeValueResponseMapper.ToResponse(attributeValue, attributeDefinition, product.Id);

        Assert.Equal(attributeValue.Id, response.Id);
        Assert.Equal(product.Id, response.ProductId);
        Assert.Equal(attributeDefinition.Id, response.AttributeDefinitionId);
        Assert.Equal("RAM", response.AttributeName);
        Assert.Equal("ram", response.AttributeSlug);
        Assert.Equal(ContractEnums.AttributeDataType.Number, response.DataType);
        Assert.Equal("8", response.Value);
    }

    [Fact]
    public void AttributeValueMapper_ToResponses_SkipsMissingDefinitions()
    {
        var product = TestProducts.Create();
        var ram = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        product.AssignAttributeValue(ram, "8GB", UtcNow);
        var definitions = new Dictionary<Guid, AttributeDefinition>();

        var responses = ProductAttributeValueResponseMapper.ToResponses(product.Id, product.AttributeValues, definitions);

        Assert.Empty(responses);
    }

    [Fact]
    public void AttributeValueMapper_ToResponses_MapsCollection()
    {
        var product = TestProducts.Create();
        var ram = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        var storage = TestAttributeDefinitions.Create(name: "Storage", slug: "storage");
        product.AssignAttributeValue(ram, "8GB", UtcNow);
        product.AssignAttributeValue(storage, "256GB", UtcNow);
        var definitions = new Dictionary<Guid, AttributeDefinition>
        {
            [ram.Id] = ram,
            [storage.Id] = storage
        };

        var responses = ProductAttributeValueResponseMapper.ToResponses(product.Id, product.AttributeValues, definitions);

        Assert.Equal(2, responses.Count);
    }

    // ---- CatalogImageSummaryMapper ----

    [Fact]
    public void ImageSummaryMapper_MapsFields_AndOrders()
    {
        var product = TestProducts.Create();
        product.AddImage("https://example.com/a.jpg", null, false, 1, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", null, true, 0, null, UtcNow);

        var summaries = CatalogImageSummaryMapper.ToSummaries(product.Images);

        Assert.Equal("https://example.com/b.jpg", summaries.ElementAt(0).ImageUrl);
        Assert.True(summaries.ElementAt(0).IsPrimary);
        Assert.Equal("https://example.com/a.jpg", summaries.ElementAt(1).ImageUrl);
    }

    // ---- CatalogVariantSummaryMapper ----

    [Fact]
    public void VariantSummaryMapper_MapsFields()
    {
        var variant = TestVariants.Create(sku: "SKU-1", name: "128GB");

        var summary = CatalogVariantSummaryMapper.ToSummary(variant);

        Assert.Equal(variant.Id, summary.Id);
        Assert.Equal("SKU-1", summary.Sku);
        Assert.Equal("128GB", summary.Name);
        Assert.Equal(variant.UnitPrice.Amount, summary.UnitPriceAmount);
        Assert.Equal(variant.UnitPrice.Currency, summary.Currency);
    }

    // ---- CatalogSpecificationSummaryMapper ----

    [Fact]
    public void SpecificationSummaryMapper_MapsFields()
    {
        var product = TestProducts.Create();
        product.AddSpecification("Color", "Black", UtcNow);
        var specification = product.Specifications.Single();

        var summary = CatalogSpecificationSummaryMapper.ToSummary(specification);

        Assert.Equal(specification.Id, summary.Id);
        Assert.Equal("Color", summary.Name);
        Assert.Equal("Black", summary.Value);
    }

    // ---- CatalogAttributeSummaryMapper ----

    [Fact]
    public void AttributeSummaryMapper_MapsFields()
    {
        var product = TestProducts.Create();
        var ram = TestAttributeDefinitions.Create(name: "RAM", slug: "ram", dataType: AttributeDataType.Text);
        product.AssignAttributeValue(ram, "8GB", UtcNow);

        var summary = CatalogAttributeSummaryMapper.ToSummary(product.AttributeValues.Single(), ram);

        Assert.Equal(ram.Id, summary.AttributeDefinitionId);
        Assert.Equal("RAM", summary.Name);
        Assert.Equal("ram", summary.Slug);
        Assert.Equal(ContractEnums.AttributeDataType.Text, summary.DataType);
        Assert.Equal("8GB", summary.Value);
    }

    // ---- CatalogProductListItemMapper ----

    [Fact]
    public void ProductListItemMapper_MapsFields_WithPrimaryImage()
    {
        var product = TestProducts.Create(name: "Phone", slug: "phone");
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", "Alt", true, 1, null, UtcNow);

        var item = CatalogProductListItemMapper.ToListItem(product);

        Assert.Equal(product.Id, item.Id);
        Assert.Equal("Phone", item.Name);
        Assert.Equal("phone", item.Slug);
        Assert.Equal(product.CategoryId, item.CategoryId);
        Assert.NotNull(item.PrimaryImage);
        Assert.Equal("https://example.com/b.jpg", item.PrimaryImage.ImageUrl);
        Assert.Equal("Alt", item.PrimaryImage.AltText);
    }

    [Fact]
    public void ProductListItemMapper_WithoutPrimaryImage_HasNullPrimary()
    {
        var product = TestProducts.Create();

        var item = CatalogProductListItemMapper.ToListItem(product);

        Assert.Null(item.PrimaryImage);
    }

    [Fact]
    public void ProductListItemMapper_ToListItems_MapsCollection()
    {
        var products = new[] { TestProducts.Create(), TestProducts.Create(name: "Laptop", slug: "laptop") };

        var items = CatalogProductListItemMapper.ToListItems(products);

        Assert.Equal(2, items.Count);
    }

    // ---- CatalogCategoryTreeMapper ----

    [Fact]
    public void CategoryTreeMapper_BuildsHierarchy_SortedByName()
    {
        var root = TestCategories.Create(name: "Root", slug: "root");
        var alpha = TestCategories.Create(name: "Alpha", slug: "alpha", parentId: root.Id);
        var beta = TestCategories.Create(name: "Beta", slug: "beta", parentId: root.Id);
        var child = TestCategories.Create(name: "Child", slug: "child", parentId: beta.Id);
        var categories = new[] { beta, root, alpha, child };

        var tree = CatalogCategoryTreeMapper.ToTree(categories);

        var rootNode = Assert.Single(tree);
        Assert.Equal(root.Id, rootNode.Id);
        Assert.Equal("Alpha", rootNode.Children.ElementAt(0).Name);
        Assert.Equal("Beta", rootNode.Children.ElementAt(1).Name);
        var betaNode = rootNode.Children.ElementAt(1);
        Assert.Equal(child.Id, Assert.Single(betaNode.Children).Id);
    }

    [Fact]
    public void CategoryTreeMapper_Empty_ReturnsEmpty()
    {
        var tree = CatalogCategoryTreeMapper.ToTree(Array.Empty<Category>());

        Assert.Empty(tree);
    }

    // ---- CatalogProductDetailsMapper ----

    [Fact]
    public void ProductDetailsMapper_MapsAllSections()
    {
        var product = TestProducts.Create(name: "Phone", slug: "phone", description: "Desc", seo: SeoMetadata.Create("Title", "D", new[] { "k" }));
        product.AddImage("https://example.com/a.jpg", null, true, 0, null, UtcNow);
        product.AddSpecification("Color", "Black", UtcNow);
        var ram = TestAttributeDefinitions.Create(name: "RAM", slug: "ram");
        product.AssignAttributeValue(ram, "8GB", UtcNow);
        var variant = TestVariants.Create(productId: product.Id, sku: "SKU-1", name: "128GB");
        var definitions = new Dictionary<Guid, AttributeDefinition> { [ram.Id] = ram };

        var details = CatalogProductDetailsMapper.ToDetails(product, new[] { variant }, definitions);

        Assert.Equal(product.Id, details.Id);
        Assert.Equal("Phone", details.Name);
        Assert.NotNull(details.Seo);
        Assert.Single(details.Images);
        Assert.Single(details.Variants);
        Assert.Single(details.Attributes);
        Assert.Single(details.Specifications);
        Assert.Equal(product.CategoryId, details.CategoryId);
    }
}
