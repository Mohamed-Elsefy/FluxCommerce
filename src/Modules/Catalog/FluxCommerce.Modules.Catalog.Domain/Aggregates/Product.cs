using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class Product : AggregateRoot<Guid>
{
    public const int NameMaxLength = 200;

    public const int DescriptionMaxLength = 2000;

    public string Name { get; private set; }

    public Slug Slug { get; private set; }

    public string? Description { get; private set; }

    public SeoMetadata? Seo { get; private set; }

    public Guid CategoryId { get; private set; }

    public ProductStatus Status { get; private set; }

    public ProductVisibility Visibility { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime? DeletedAtUtc { get; private set; }

    private readonly List<ProductAttributeValue> _attributeValues = [];

    public IReadOnlyCollection<ProductAttributeValue> AttributeValues => _attributeValues;

    private readonly List<ProductSpecification> _specifications = [];

    public IReadOnlyCollection<ProductSpecification> Specifications => _specifications;

    private readonly List<ProductImage> _images = [];

    public IReadOnlyCollection<ProductImage> Images => _images;

    private Product(
        Guid id,
        string name,
        Slug slug,
        Guid categoryId,
        DateTime createdAtUtc,
        string? description,
        SeoMetadata? seo,
        ProductVisibility visibility)
        : base(id)
    {
        Name = ValidateName(name, nameof(name), NameMaxLength);
        Slug = slug;
        CategoryId = categoryId;
        Description = NormalizeDescription(description, nameof(description));
        Seo = seo;
        Status = ProductStatus.Draft;
        Visibility = visibility;
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        DeletedAtUtc = null;
    }

    #pragma warning disable CS8618
    private Product() { }
    #pragma warning restore CS8618

    public static Product Create(
        Guid id,
        string name,
        Slug slug,
        Guid categoryId,
        DateTime utcNow,
        string? description = null,
        SeoMetadata? seo = null,
        ProductVisibility visibility = ProductVisibility.Visible)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A product id must be provided.", nameof(id));

        if (categoryId == Guid.Empty)
            throw new ArgumentException("A category id must be provided.", nameof(categoryId));

        var product = new Product(id, name, slug, categoryId, utcNow, description, seo, visibility);

        product.AddDomainEvent(new ProductCreated(id));

        return product;
    }

    private static string ValidateName(string? value, string paramName, int maxLength)
    {
        var name = value?.Trim();

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException($"A {paramName} is required.", paramName);

        if (name.Length > maxLength)
            throw new ArgumentException($"{paramName} must not exceed {maxLength} characters.", paramName);

        return name;
    }

    private static string? NormalizeDescription(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var description = value.Trim();

        if (description.Length > DescriptionMaxLength)
            throw new ArgumentException($"{paramName} must not exceed {DescriptionMaxLength} characters.", paramName);

        return description;
    }

    public DomainResult Rename(string name)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var normalizedName = ValidateName(name, nameof(name), NameMaxLength);

        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return DomainResult.Success();

        Name = normalizedName;

        return DomainResult.Success();
    }

    public DomainResult ChangeDescription(string? description)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        Description = NormalizeDescription(description, nameof(description));

        return DomainResult.Success();
    }

    public DomainResult ChangeSlug(Slug slug)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        Slug = slug;

        return DomainResult.Success();
    }

    public DomainResult ChangeSeoMetadata(SeoMetadata? seo)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        Seo = seo;

        return DomainResult.Success();
    }

    public DomainResult ChangeCategory(Guid categoryId)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (categoryId == Guid.Empty)
            throw new ArgumentException("A category id is required.", nameof(categoryId));

        if (CategoryId == categoryId)
            return DomainResult.Success();

        CategoryId = categoryId;

        AddDomainEvent(new ProductCategoryChanged(Id, categoryId));

        return DomainResult.Success();
    }

    public DomainResult ChangeVisibility(ProductVisibility visibility)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (Visibility == visibility)
            return DomainResult.Success();

        Visibility = visibility;

        return DomainResult.Success();
    }

    public DomainResult AssignAttributeValue(AttributeDefinition attributeDefinition, string value, DateTime utcNow)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var definitionResult = ValidateAttributeDefinition(attributeDefinition);

        if (definitionResult.IsFailure)
            return definitionResult;

        if (_attributeValues.Any(attributeValue => attributeValue.AttributeDefinitionId == attributeDefinition.Id))
            return DomainResult.Failure(ProductErrors.DuplicateAttributeValue);

        var valueResult = ValidateValue(attributeDefinition.DataType, value);

        if (valueResult.IsFailure)
            return valueResult;

        _attributeValues.Add(new ProductAttributeValue(Guid.NewGuid(), attributeDefinition.Id, value.Trim(), utcNow));

        AddDomainEvent(new ProductAttributeValueAssigned(Id, attributeDefinition.Id, value.Trim()));

        return DomainResult.Success();
    }

    public DomainResult UpdateAttributeValue(Guid attributeDefinitionId, AttributeDefinition attributeDefinition, string value)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var definitionResult = ValidateAttributeDefinition(attributeDefinition);

        if (definitionResult.IsFailure)
            return definitionResult;

        var attributeValue = _attributeValues.FirstOrDefault(attributeValue => attributeValue.AttributeDefinitionId == attributeDefinitionId);

        if (attributeValue is null)
            return DomainResult.Failure(ProductErrors.AttributeValueNotFound);

        var valueResult = ValidateValue(attributeDefinition.DataType, value);

        if (valueResult.IsFailure)
            return valueResult;

        attributeValue.ChangeValue(value.Trim());

        AddDomainEvent(new ProductAttributeValueUpdated(Id, attributeDefinitionId, value.Trim()));

        return DomainResult.Success();
    }

    public DomainResult RemoveAttributeValue(Guid attributeDefinitionId)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var attributeValue = _attributeValues.FirstOrDefault(attributeValue => attributeValue.AttributeDefinitionId == attributeDefinitionId);

        if (attributeValue is null)
            return DomainResult.Failure(ProductErrors.AttributeValueNotFound);

        _attributeValues.Remove(attributeValue);

        AddDomainEvent(new ProductAttributeValueRemoved(Id, attributeDefinitionId));

        return DomainResult.Success();
    }

    private DomainResult ValidateAttributeDefinition(AttributeDefinition attributeDefinition)
    {
        if (attributeDefinition.IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedAttributeDefinitionCannotBeUsed);

        if (attributeDefinition.Status != AttributeStatus.Active)
            return DomainResult.Failure(ProductErrors.InactiveAttributeDefinitionCannotBeUsed);

        if (attributeDefinition.CategoryId is { } categoryId && categoryId != CategoryId)
            return DomainResult.Failure(ProductErrors.AttributeDefinitionCategoryMismatch);

        return DomainResult.Success();
    }

    private static DomainResult ValidateValue(AttributeDataType dataType, string? value)
    {
        var normalizedValue = value?.Trim();

        if (string.IsNullOrEmpty(normalizedValue))
            return DomainResult.Failure(ProductErrors.AttributeValueRequired);

        if (normalizedValue.Length > ProductAttributeValue.ValueMaxLength)
            return DomainResult.Failure(ProductErrors.AttributeValueTooLong);

        var isValid = dataType switch
        {
            AttributeDataType.Text => true,
            AttributeDataType.Number => long.TryParse(normalizedValue, out _),
            AttributeDataType.Decimal => decimal.TryParse(normalizedValue, out _),
            AttributeDataType.Boolean => bool.TryParse(normalizedValue, out _),
            AttributeDataType.Date => DateTime.TryParse(normalizedValue, out _),
            AttributeDataType.DateTime => DateTime.TryParse(normalizedValue, out _),
            _ => false
        };

        return isValid
            ? DomainResult.Success()
            : DomainResult.Failure(ProductErrors.InvalidAttributeValue);
    }

    public DomainResult AddSpecification(string name, string value, DateTime utcNow)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var nameResult = ValidateSpecificationName(name);

        if (nameResult.IsFailure)
            return nameResult;

        var valueResult = ValidateSpecificationValue(value);

        if (valueResult.IsFailure)
            return valueResult;

        var normalizedName = name.Trim();
        var normalizedValue = value.Trim();

        if (_specifications.Any(specification => specification.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            return DomainResult.Failure(ProductErrors.DuplicateSpecification);

        var specification = new ProductSpecification(Guid.NewGuid(), normalizedName, normalizedValue, utcNow);

        _specifications.Add(specification);

        AddDomainEvent(new ProductSpecificationCreated(Id, specification.Id, normalizedName, normalizedValue));

        return DomainResult.Success();
    }

    public DomainResult UpdateSpecification(Guid specificationId, string name, string value)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var nameResult = ValidateSpecificationName(name);

        if (nameResult.IsFailure)
            return nameResult;

        var valueResult = ValidateSpecificationValue(value);

        if (valueResult.IsFailure)
            return valueResult;

        var specification = _specifications.FirstOrDefault(specification => specification.Id == specificationId);

        if (specification is null)
            return DomainResult.Failure(ProductErrors.SpecificationNotFound);

        var normalizedName = name.Trim();
        var normalizedValue = value.Trim();

        if (_specifications.Any(specification => specification.Id != specificationId && specification.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            return DomainResult.Failure(ProductErrors.DuplicateSpecification);

        specification.ChangeValue(normalizedName, normalizedValue);

        AddDomainEvent(new ProductSpecificationUpdated(Id, specificationId, normalizedName, normalizedValue));

        return DomainResult.Success();
    }

    public DomainResult RemoveSpecification(Guid specificationId)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var specification = _specifications.FirstOrDefault(specification => specification.Id == specificationId);

        if (specification is null)
            return DomainResult.Failure(ProductErrors.SpecificationNotFound);

        _specifications.Remove(specification);

        AddDomainEvent(new ProductSpecificationRemoved(Id, specificationId));

        return DomainResult.Success();
    }

    private static DomainResult ValidateSpecificationName(string? name)
    {
        var normalizedName = name?.Trim();

        if (string.IsNullOrEmpty(normalizedName))
            return DomainResult.Failure(ProductErrors.SpecificationNameRequired);

        if (normalizedName.Length > ProductSpecification.NameMaxLength)
            return DomainResult.Failure(ProductErrors.SpecificationNameTooLong);

        return DomainResult.Success();
    }

    private static DomainResult ValidateSpecificationValue(string? value)
    {
        var normalizedValue = value?.Trim();

        if (string.IsNullOrEmpty(normalizedValue))
            return DomainResult.Failure(ProductErrors.SpecificationValueRequired);

        if (normalizedValue.Length > ProductSpecification.ValueMaxLength)
            return DomainResult.Failure(ProductErrors.SpecificationValueTooLong);

        return DomainResult.Success();
    }

    public DomainResult AddImage(
        string imageUrl,
        string? altText,
        bool isPrimary,
        int displayOrder,
        Dictionary<string, string>? metadata,
        DateTime utcNow)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var imageUrlResult = ValidateImageUrl(imageUrl);

        if (imageUrlResult.IsFailure)
            return imageUrlResult;

        var altTextResult = ValidateAltText(altText);

        if (altTextResult.IsFailure)
            return altTextResult;

        if (displayOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(displayOrder), "A display order cannot be negative.");

        if (isPrimary)
        {
            foreach (var existingImage in _images.Where(image => image.IsPrimary))
                existingImage.ClearPrimary();
        }

        var image = new ProductImage(Guid.NewGuid(), imageUrl.Trim(), NormalizeAltText(altText), displayOrder, isPrimary, metadata, utcNow);

        _images.Add(image);

        AddDomainEvent(new ProductImageAdded(Id, image.Id, image.ImageUrl, image.IsPrimary));

        return DomainResult.Success();
    }

    public DomainResult UpdateImage(
        Guid imageId,
        string imageUrl,
        string? altText,
        int displayOrder,
        Dictionary<string, string>? metadata)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var imageUrlResult = ValidateImageUrl(imageUrl);

        if (imageUrlResult.IsFailure)
            return imageUrlResult;

        var altTextResult = ValidateAltText(altText);

        if (altTextResult.IsFailure)
            return altTextResult;

        if (displayOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(displayOrder), "A display order cannot be negative.");

        var image = _images.FirstOrDefault(image => image.Id == imageId);

        if (image is null)
            return DomainResult.Failure(ProductErrors.ProductImageNotFound);

        image.ChangeDetails(imageUrl.Trim(), NormalizeAltText(altText), displayOrder, metadata);

        AddDomainEvent(new ProductImageUpdated(Id, imageId, image.ImageUrl, image.AltText, image.DisplayOrder));

        return DomainResult.Success();
    }

    public DomainResult RemoveImage(Guid imageId)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var image = _images.FirstOrDefault(image => image.Id == imageId);

        if (image is null)
            return DomainResult.Failure(ProductErrors.ProductImageNotFound);

        _images.Remove(image);

        AddDomainEvent(new ProductImageRemoved(Id, imageId));

        return DomainResult.Success();
    }

    public DomainResult SetPrimaryImage(Guid imageId)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        var image = _images.FirstOrDefault(image => image.Id == imageId);

        if (image is null)
            return DomainResult.Failure(ProductErrors.ProductImageNotFound);

        foreach (var existingImage in _images.Where(existingImage => existingImage.IsPrimary && existingImage.Id != imageId))
            existingImage.ClearPrimary();

        image.SetAsPrimary();

        AddDomainEvent(new ProductPrimaryImageChanged(Id, imageId));

        return DomainResult.Success();
    }

    public DomainResult ReorderImages(IReadOnlyCollection<Guid> orderedImageIds)
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (orderedImageIds is null)
            throw new ArgumentNullException(nameof(orderedImageIds));

        var requestedIds = orderedImageIds.Distinct().ToHashSet();

        if (requestedIds.Count != orderedImageIds.Count)
            return DomainResult.Failure(ProductErrors.InvalidImageOrder);

        if (_images.Count != requestedIds.Count)
            return DomainResult.Failure(ProductErrors.InvalidImageOrder);

        var imageLookup = _images.ToDictionary(image => image.Id);

        if (requestedIds.Any(imageId => !imageLookup.ContainsKey(imageId)))
            return DomainResult.Failure(ProductErrors.InvalidImageOrder);

        var displayOrder = 0;

        foreach (var imageId in orderedImageIds)
            imageLookup[imageId].ChangeDisplayOrder(displayOrder++);

        AddDomainEvent(new ProductImagesReordered(Id));

        return DomainResult.Success();
    }

    private static DomainResult ValidateImageUrl(string? imageUrl)
    {
        var normalizedImageUrl = imageUrl?.Trim();

        if (string.IsNullOrEmpty(normalizedImageUrl))
            return DomainResult.Failure(ProductErrors.ImageUrlRequired);

        if (normalizedImageUrl.Length > ProductImage.ImageUrlMaxLength)
            return DomainResult.Failure(ProductErrors.ImageUrlTooLong);

        return DomainResult.Success();
    }

    private static DomainResult ValidateAltText(string? altText)
    {
        var normalizedAltText = altText?.Trim();

        if (normalizedAltText is not null && normalizedAltText.Length > ProductImage.AltTextMaxLength)
            return DomainResult.Failure(ProductErrors.AltTextTooLong);

        return DomainResult.Success();
    }

    private static string? NormalizeAltText(string? altText)
    {
        if (string.IsNullOrWhiteSpace(altText))
            return null;

        return altText.Trim();
    }

    public DomainResult Activate()
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (Status == ProductStatus.Archived)
            return DomainResult.Failure(ProductErrors.InvalidTransition);

        if (Status == ProductStatus.Active)
            return DomainResult.Success();

        Status = ProductStatus.Active;

        AddDomainEvent(new ProductActivated(Id));

        return DomainResult.Success();
    }

    public DomainResult Deactivate()
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (Status == ProductStatus.Archived)
            return DomainResult.Failure(ProductErrors.InvalidTransition);

        if (Status == ProductStatus.Active)
        {
            Status = ProductStatus.Inactive;

            return DomainResult.Success();
        }

        return DomainResult.Success();
    }

    public DomainResult Archive()
    {
        if (IsDeleted)
            return DomainResult.Failure(ProductErrors.DeletedProductCannotBeModified);

        if (Status == ProductStatus.Archived)
            return DomainResult.Success();

        Status = ProductStatus.Archived;

        AddDomainEvent(new ProductArchived(Id));

        return DomainResult.Success();
    }

    public DomainResult SoftDelete(DateTime utcNow)
    {
        if (IsDeleted)
            return DomainResult.Success();

        IsDeleted = true;
        DeletedAtUtc = utcNow;

        return DomainResult.Success();
    }

    public DomainResult Restore()
    {
        if (!IsDeleted)
            return DomainResult.Success();

        IsDeleted = false;
        DeletedAtUtc = null;

        return DomainResult.Success();
    }
}
