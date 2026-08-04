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
