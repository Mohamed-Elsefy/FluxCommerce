using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class ProductAggregateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid CategoryId = Guid.NewGuid();

    private static AttributeDefinition CreateActiveDefinition(
        Guid? categoryId = null,
        AttributeDataType dataType = AttributeDataType.Text) =>
        AttributeDefinition.Create(
            Guid.NewGuid(),
            "RAM",
            new Slug("ram"),
            dataType,
            UtcNow,
            categoryId: categoryId);

    private static Product CreateProduct() =>
        Product.Create(Guid.NewGuid(), "Smartphone", new Slug("smartphone"), CategoryId, UtcNow);

    // ---- Creation ----

    [Fact]
    public void Create_CreatesDraftProduct_AndRaisesEvent()
    {
        var product = Product.Create(
            Guid.NewGuid(),
            "Smartphone",
            new Slug("smartphone"),
            CategoryId,
            UtcNow,
            "A phone",
            SeoMetadata.Create("Smartphone", "Phone description", ["phone"]),
            ProductVisibility.Visible);

        Assert.Equal("Smartphone", product.Name);
        Assert.Equal("smartphone", product.Slug.Value);
        Assert.Equal("A phone", product.Description);
        Assert.NotNull(product.Seo);
        Assert.Equal(CategoryId, product.CategoryId);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal(ProductVisibility.Visible, product.Visibility);
        Assert.False(product.IsDeleted);
        Assert.Equal(UtcNow, product.CreatedAtUtc);
        Assert.True(product.GetDomainEvents().OfType<ProductCreated>().Any());
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Product.Create(Guid.Empty, "Smartphone", new Slug("smartphone"), CategoryId, UtcNow));
    }

    [Fact]
    public void Create_WithEmptyCategoryId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Product.Create(Guid.NewGuid(), "Smartphone", new Slug("smartphone"), Guid.Empty, UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            Product.Create(Guid.NewGuid(), name!, new Slug("smartphone"), CategoryId, UtcNow));
    }

    [Fact]
    public void Create_WithNameOverMaxLength_Throws()
    {
        var name = string.Concat(Enumerable.Repeat("a", Product.NameMaxLength + 1));

        Assert.Throws<ArgumentException>(() =>
            Product.Create(Guid.NewGuid(), name, new Slug("smartphone"), CategoryId, UtcNow));
    }

    [Fact]
    public void Create_TrimsNameAndDescription()
    {
        var product = Product.Create(
            Guid.NewGuid(),
            "  Smartphone  ",
            new Slug("smartphone"),
            CategoryId,
            UtcNow,
            "  A phone  ");

        Assert.Equal("Smartphone", product.Name);
        Assert.Equal("A phone", product.Description);
    }

    // ---- Mutations ----

    [Fact]
    public void Rename_ChangesName()
    {
        var product = CreateProduct();

        var result = product.Rename("Phone");

        Assert.True(result.IsSuccess);
        Assert.Equal("Phone", product.Name);
    }

    [Fact]
    public void Rename_WithSameName_IsNoOp()
    {
        var product = CreateProduct();

        var result = product.Rename("  Smartphone  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Smartphone", product.Name);
    }

    [Fact]
    public void ChangeSlug_UpdatesSlug()
    {
        var product = CreateProduct();

        var result = product.ChangeSlug(new Slug("phone"));

        Assert.True(result.IsSuccess);
        Assert.Equal("phone", product.Slug.Value);
    }

    [Fact]
    public void ChangeSeoMetadata_UpdatesSeo()
    {
        var product = CreateProduct();

        var result = product.ChangeSeoMetadata(SeoMetadata.Create("Title", "Description"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Title", product.Seo!.Title);
    }

    [Fact]
    public void ChangeCategory_UpdatesCategory_AndRaisesEvent()
    {
        var product = CreateProduct();

        var newCategoryId = Guid.NewGuid();
        var result = product.ChangeCategory(newCategoryId);

        Assert.True(result.IsSuccess);
        Assert.Equal(newCategoryId, product.CategoryId);
        Assert.True(product.GetDomainEvents().OfType<ProductCategoryChanged>().Any());
    }

    [Fact]
    public void ChangeCategory_ToSameCategory_IsNoOp()
    {
        var product = CreateProduct();

        var result = product.ChangeCategory(CategoryId);

        Assert.True(result.IsSuccess);
        Assert.Empty(product.GetDomainEvents().OfType<ProductCategoryChanged>());
    }

    [Fact]
    public void ChangeCategory_WithEmptyId_Throws()
    {
        var product = CreateProduct();

        Assert.Throws<ArgumentException>(() => product.ChangeCategory(Guid.Empty));
    }

    [Fact]
    public void ChangeVisibility_UpdatesVisibility()
    {
        var product = CreateProduct();

        var result = product.ChangeVisibility(ProductVisibility.Hidden);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductVisibility.Hidden, product.Visibility);
    }

    // ---- Attribute values ----

    [Fact]
    public void AssignAttributeValue_AddsValue_AndRaisesEvent()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();

        var result = product.AssignAttributeValue(definition, " 8GB ", UtcNow);

        Assert.True(result.IsSuccess);
        var value = Assert.Single(product.AttributeValues);
        Assert.Equal(definition.Id, value.AttributeDefinitionId);
        Assert.Equal("8GB", value.Value);
        Assert.True(product.GetDomainEvents().OfType<ProductAttributeValueAssigned>().Any());
    }

    [Fact]
    public void AssignAttributeValue_WithDuplicateDefinition_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        product.AssignAttributeValue(definition, "8GB", UtcNow);

        var result = product.AssignAttributeValue(definition, "16GB", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.DuplicateAttributeValue, result.Error);
    }

    [Fact]
    public void AssignAttributeValue_WithDeletedDefinition_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        definition.SoftDelete(UtcNow);

        var result = product.AssignAttributeValue(definition, "8GB", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.DeletedAttributeDefinitionCannotBeUsed, result.Error);
    }

    [Fact]
    public void AssignAttributeValue_WithInactiveDefinition_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        definition.Deactivate();

        var result = product.AssignAttributeValue(definition, "8GB", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InactiveAttributeDefinitionCannotBeUsed, result.Error);
    }

    [Fact]
    public void AssignAttributeValue_WithCategoryMismatch_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition(categoryId: Guid.NewGuid());

        var result = product.AssignAttributeValue(definition, "8GB", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AttributeDefinitionCategoryMismatch, result.Error);
    }

    [Fact]
    public void AssignAttributeValue_WithEmptyValue_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();

        var result = product.AssignAttributeValue(definition, "  ", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AttributeValueRequired, result.Error);
    }

    [Fact]
    public void AssignAttributeValue_WithValueOverMaxLength_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        var value = string.Concat(Enumerable.Repeat("a", ProductAttributeValue.ValueMaxLength + 1));

        var result = product.AssignAttributeValue(definition, value, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AttributeValueTooLong, result.Error);
    }

    [Theory]
    [InlineData(AttributeDataType.Number, "abc")]
    [InlineData(AttributeDataType.Decimal, "not-a-decimal")]
    [InlineData(AttributeDataType.Boolean, "not-a-bool")]
    public void AssignAttributeValue_WithInvalidValueForType_Fails(AttributeDataType dataType, string value)
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition(dataType: dataType);

        var result = product.AssignAttributeValue(definition, value, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidAttributeValue, result.Error);
    }

    [Theory]
    [InlineData(AttributeDataType.Number, "123")]
    [InlineData(AttributeDataType.Decimal, "12.5")]
    [InlineData(AttributeDataType.Boolean, "true")]
    public void AssignAttributeValue_WithValidValueForType_Succeeds(AttributeDataType dataType, string value)
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition(dataType: dataType);

        var result = product.AssignAttributeValue(definition, value, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, Assert.Single(product.AttributeValues).Value);
    }

    [Fact]
    public void UpdateAttributeValue_UpdatesExistingValue_AndRaisesEvent()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        product.AssignAttributeValue(definition, "8GB", UtcNow);

        var result = product.UpdateAttributeValue(definition.Id, definition, "16GB");

        Assert.True(result.IsSuccess);
        Assert.Equal("16GB", Assert.Single(product.AttributeValues).Value);
        Assert.True(product.GetDomainEvents().OfType<ProductAttributeValueUpdated>().Any());
    }

    [Fact]
    public void UpdateAttributeValue_WhenNotAssigned_Fails()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();

        var result = product.UpdateAttributeValue(definition.Id, definition, "16GB");

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AttributeValueNotFound, result.Error);
    }

    [Fact]
    public void RemoveAttributeValue_RemovesValue_AndRaisesEvent()
    {
        var product = CreateProduct();
        var definition = CreateActiveDefinition();
        product.AssignAttributeValue(definition, "8GB", UtcNow);

        var result = product.RemoveAttributeValue(definition.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(product.AttributeValues);
        Assert.True(product.GetDomainEvents().OfType<ProductAttributeValueRemoved>().Any());
    }

    [Fact]
    public void RemoveAttributeValue_WhenNotAssigned_Fails()
    {
        var product = CreateProduct();

        var result = product.RemoveAttributeValue(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AttributeValueNotFound, result.Error);
    }

    // ---- Specifications ----

    [Fact]
    public void AddSpecification_AddsSpecification_AndRaisesEvent()
    {
        var product = CreateProduct();

        var result = product.AddSpecification("Screen", "6.1 inches", UtcNow);

        Assert.True(result.IsSuccess);
        var specification = Assert.Single(product.Specifications);
        Assert.Equal("Screen", specification.Name);
        Assert.Equal("6.1 inches", specification.Value);
        Assert.True(product.GetDomainEvents().OfType<ProductSpecificationCreated>().Any());
    }

    [Fact]
    public void AddSpecification_WithDuplicateName_IsCaseInsensitive_Fails()
    {
        var product = CreateProduct();
        product.AddSpecification("Screen", "6.1 inches", UtcNow);

        var result = product.AddSpecification("screen", "6.7 inches", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.DuplicateSpecification, result.Error);
    }

    [Fact]
    public void UpdateSpecification_UpdatesValue_AndRaisesEvent()
    {
        var product = CreateProduct();
        product.AddSpecification("Screen", "6.1 inches", UtcNow);
        var specification = product.Specifications.Single();

        var result = product.UpdateSpecification(specification.Id, "Screen", "6.7 inches");

        Assert.True(result.IsSuccess);
        Assert.Equal("6.7 inches", specification.Value);
        Assert.True(product.GetDomainEvents().OfType<ProductSpecificationUpdated>().Any());
    }

    [Fact]
    public void UpdateSpecification_WhenNotFound_Fails()
    {
        var product = CreateProduct();

        var result = product.UpdateSpecification(Guid.NewGuid(), "Screen", "6.7 inches");

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SpecificationNotFound, result.Error);
    }

    [Fact]
    public void UpdateSpecification_WithDuplicateName_Fails()
    {
        var product = CreateProduct();
        product.AddSpecification("Screen", "6.1 inches", UtcNow);
        product.AddSpecification("Battery", "4000 mAh", UtcNow);
        var battery = product.Specifications.Single(specification => specification.Name == "Battery");

        var result = product.UpdateSpecification(battery.Id, "Screen", "Changed");

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.DuplicateSpecification, result.Error);
    }

    [Fact]
    public void RemoveSpecification_RemovesSpecification_AndRaisesEvent()
    {
        var product = CreateProduct();
        product.AddSpecification("Screen", "6.1 inches", UtcNow);
        var specification = product.Specifications.Single();

        var result = product.RemoveSpecification(specification.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(product.Specifications);
        Assert.True(product.GetDomainEvents().OfType<ProductSpecificationRemoved>().Any());
    }

    [Fact]
    public void RemoveSpecification_WhenNotFound_Fails()
    {
        var product = CreateProduct();

        var result = product.RemoveSpecification(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SpecificationNotFound, result.Error);
    }

    [Fact]
    public void AddSpecification_WithEmptyName_Fails()
    {
        var product = CreateProduct();

        var result = product.AddSpecification("  ", "Value", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SpecificationNameRequired, result.Error);
    }

    [Fact]
    public void AddSpecification_WithValueOverMaxLength_Fails()
    {
        var product = CreateProduct();
        var value = string.Concat(Enumerable.Repeat("a", ProductSpecification.ValueMaxLength + 1));

        var result = product.AddSpecification("Screen", value, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.SpecificationValueTooLong, result.Error);
    }

    // ---- Images ----

    [Fact]
    public void AddImage_AddsImage_AndRaisesEvent()
    {
        var product = CreateProduct();

        var result = product.AddImage("https://example.com/phone.jpg", "A phone", false, 0, null, UtcNow);

        Assert.True(result.IsSuccess);
        var image = Assert.Single(product.Images);
        Assert.Equal("https://example.com/phone.jpg", image.ImageUrl);
        Assert.Equal("A phone", image.AltText);
        Assert.False(image.IsPrimary);
        Assert.True(product.GetDomainEvents().OfType<ProductImageAdded>().Any());
    }

    [Fact]
    public void AddPrimaryImage_ClearsOtherPrimaryImages()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/first.jpg", null, true, 0, null, UtcNow);
        product.AddImage("https://example.com/second.jpg", null, true, 1, null, UtcNow);

        var primaryImages = product.Images.Where(image => image.IsPrimary);

        Assert.Single(primaryImages);
        Assert.True(product.Images.Single(image => image.ImageUrl == "https://example.com/second.jpg").IsPrimary);
    }

    [Fact]
    public void AddImage_WithEmptyUrl_Fails()
    {
        var product = CreateProduct();

        var result = product.AddImage("   ", null, false, 0, null, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.ImageUrlRequired, result.Error);
    }

    [Fact]
    public void AddImage_WithUrlOverMaxLength_Fails()
    {
        var product = CreateProduct();
        var url = "https://example.com/" + string.Concat(Enumerable.Repeat("a", ProductImage.ImageUrlMaxLength));

        var result = product.AddImage(url, null, false, 0, null, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.ImageUrlTooLong, result.Error);
    }

    [Fact]
    public void AddImage_WithAltTextOverMaxLength_Fails()
    {
        var product = CreateProduct();
        var altText = string.Concat(Enumerable.Repeat("a", ProductImage.AltTextMaxLength + 1));

        var result = product.AddImage("https://example.com/phone.jpg", altText, false, 0, null, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.AltTextTooLong, result.Error);
    }

    [Fact]
    public void AddImage_WithNegativeDisplayOrder_Throws()
    {
        var product = CreateProduct();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            product.AddImage("https://example.com/phone.jpg", null, false, -1, null, UtcNow));
    }

    [Fact]
    public void UpdateImage_UpdatesDetails_AndRaisesEvent()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/phone.jpg", "Old", false, 0, null, UtcNow);
        var image = product.Images.Single();

        var result = product.UpdateImage(image.Id, "https://example.com/new.jpg", "New", 2, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://example.com/new.jpg", image.ImageUrl);
        Assert.Equal("New", image.AltText);
        Assert.Equal(2, image.DisplayOrder);
        Assert.True(product.GetDomainEvents().OfType<ProductImageUpdated>().Any());
    }

    [Fact]
    public void UpdateImage_WhenNotFound_Fails()
    {
        var product = CreateProduct();

        var result = product.UpdateImage(Guid.NewGuid(), "https://example.com/new.jpg", null, 0, null);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.ProductImageNotFound, result.Error);
    }

    [Fact]
    public void RemoveImage_RemovesImage_AndRaisesEvent()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/phone.jpg", null, false, 0, null, UtcNow);
        var image = product.Images.Single();

        var result = product.RemoveImage(image.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(product.Images);
        Assert.True(product.GetDomainEvents().OfType<ProductImageRemoved>().Any());
    }

    [Fact]
    public void RemoveImage_WhenNotFound_Fails()
    {
        var product = CreateProduct();

        var result = product.RemoveImage(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.ProductImageNotFound, result.Error);
    }

    [Fact]
    public void SetPrimaryImage_SetsPrimary_AndClearsOthers()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/first.jpg", null, true, 0, null, UtcNow);
        product.AddImage("https://example.com/second.jpg", null, false, 1, null, UtcNow);
        var second = product.Images.Single(image => image.ImageUrl == "https://example.com/second.jpg");

        var result = product.SetPrimaryImage(second.Id);

        Assert.True(result.IsSuccess);
        Assert.True(second.IsPrimary);
        Assert.False(product.Images.Single(image => image.ImageUrl == "https://example.com/first.jpg").IsPrimary);
        Assert.True(product.GetDomainEvents().OfType<ProductPrimaryImageChanged>().Any());
    }

    [Fact]
    public void SetPrimaryImage_WhenNotFound_Fails()
    {
        var product = CreateProduct();

        var result = product.SetPrimaryImage(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.ProductImageNotFound, result.Error);
    }

    [Fact]
    public void ReorderImages_ReordersByDisplayOrder_AndRaisesEvent()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, UtcNow);
        var first = product.Images.Single(image => image.ImageUrl == "https://example.com/a.jpg");
        var second = product.Images.Single(image => image.ImageUrl == "https://example.com/b.jpg");

        var result = product.ReorderImages([second.Id, first.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, second.DisplayOrder);
        Assert.Equal(1, first.DisplayOrder);
        Assert.True(product.GetDomainEvents().OfType<ProductImagesReordered>().Any());
    }

    [Fact]
    public void ReorderImages_WithDuplicateIds_Fails()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, UtcNow);
        var first = product.Images.Single(image => image.ImageUrl == "https://example.com/a.jpg");

        var result = product.ReorderImages([first.Id, first.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidImageOrder, result.Error);
    }

    [Fact]
    public void ReorderImages_WithMissingImage_Fails()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow);
        var first = product.Images.Single();

        var result = product.ReorderImages([first.Id, Guid.NewGuid()]);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidImageOrder, result.Error);
    }

    [Fact]
    public void ReorderImages_WithCountMismatch_Fails()
    {
        var product = CreateProduct();
        product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow);
        product.AddImage("https://example.com/b.jpg", null, false, 1, null, UtcNow);
        var first = product.Images.Single(image => image.ImageUrl == "https://example.com/a.jpg");

        var result = product.ReorderImages([first.Id]);

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidImageOrder, result.Error);
    }

    // ---- Lifecycle ----

    [Fact]
    public void Activate_ActivatesDraftProduct_AndRaisesEvent()
    {
        var product = CreateProduct();

        var result = product.Activate();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.True(product.GetDomainEvents().OfType<ProductActivated>().Any());
    }

    [Fact]
    public void Deactivate_DeactivatesActiveProduct()
    {
        var product = CreateProduct();
        product.Activate();

        var result = product.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Inactive, product.Status);
    }

    [Fact]
    public void Archive_ArchivesProduct_AndRaisesEvent()
    {
        var product = CreateProduct();

        var result = product.Archive();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProductStatus.Archived, product.Status);
        Assert.True(product.GetDomainEvents().OfType<ProductArchived>().Any());
    }

    [Fact]
    public void Activate_FromArchived_Fails()
    {
        var product = CreateProduct();
        product.Archive();

        var result = product.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void Deactivate_FromArchived_Fails()
    {
        var product = CreateProduct();
        product.Archive();

        var result = product.Deactivate();

        Assert.True(result.IsFailure);
        Assert.Equal(ProductErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void SoftDelete_MarksDeleted()
    {
        var product = CreateProduct();

        var result = product.SoftDelete(UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(product.IsDeleted);
        Assert.Equal(UtcNow, product.DeletedAtUtc);
    }

    [Fact]
    public void Restore_UnmarksDeleted()
    {
        var product = CreateProduct();
        product.SoftDelete(UtcNow);

        var result = product.Restore();

        Assert.True(result.IsSuccess);
        Assert.False(product.IsDeleted);
        Assert.Null(product.DeletedAtUtc);
    }

    [Fact]
    public void DeletedProduct_CannotBeModified()
    {
        var product = CreateProduct();
        product.SoftDelete(UtcNow);

        Assert.True(product.Rename("Phone").IsFailure);
        Assert.True(product.Activate().IsFailure);
        Assert.True(product.Deactivate().IsFailure);
        Assert.True(product.Archive().IsFailure);
        Assert.True(product.ChangeSlug(new Slug("phone")).IsFailure);
        Assert.True(product.ChangeVisibility(ProductVisibility.Hidden).IsFailure);
        Assert.True(product.ChangeCategory(Guid.NewGuid()).IsFailure);
        Assert.True(product.ChangeDescription("x").IsFailure);
        Assert.True(product.ChangeSeoMetadata(SeoMetadata.Create("T", "D")).IsFailure);
        var definition = CreateActiveDefinition();
        Assert.True(product.AssignAttributeValue(definition, "8GB", UtcNow).IsFailure);
        Assert.True(product.AddSpecification("Screen", "Value", UtcNow).IsFailure);
        Assert.True(product.AddImage("https://example.com/a.jpg", null, false, 0, null, UtcNow).IsFailure);
    }
}
