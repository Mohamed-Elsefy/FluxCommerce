using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class Category : AggregateRoot<Guid>
{
    public const int NameMaxLength = 100;

    public const int DescriptionMaxLength = 500;

    public string Name { get; private set; }

    public Slug Slug { get; private set; }

    public string? Description { get; private set; }

    public Guid? ParentId { get; private set; }

    public CategoryStatus Status { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime? DeletedAtUtc { get; private set; }

    private Category(
        Guid id,
        string name,
        Slug slug,
        string? description,
        Guid? parentId,
        DateTime createdAtUtc)
        : base(id)
    {
        Name = ValidateName(name, nameof(name), NameMaxLength);
        Slug = slug;
        Description = NormalizeDescription(description, nameof(description));
        ParentId = parentId;
        Status = CategoryStatus.Active;
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        DeletedAtUtc = null;
    }

    #pragma warning disable CS8618
    private Category() { }
    #pragma warning restore CS8618

    public static Category Create(
        Guid id,
        string name,
        Slug slug,
        DateTime utcNow,
        string? description = null,
        Guid? parentId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A category id must be provided.", nameof(id));

        if (parentId == id)
            throw new ArgumentException("A category cannot be its own parent.", nameof(parentId));

        var category = new Category(id, name, slug, description, parentId, utcNow);

        category.AddDomainEvent(new CategoryCreated(id));

        return category;
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
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        var normalizedName = ValidateName(name, nameof(name), NameMaxLength);

        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return DomainResult.Success();

        Name = normalizedName;

        AddDomainEvent(new CategoryRenamed(Id, Name));

        return DomainResult.Success();
    }

    public DomainResult ChangeDescription(string? description)
    {
        if (IsDeleted)
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        Description = NormalizeDescription(description, nameof(description));

        return DomainResult.Success();
    }

    public DomainResult ChangeSlug(Slug slug)
    {
        if (IsDeleted)
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        Slug = slug;

        return DomainResult.Success();
    }

    public DomainResult ChangeParent(Guid? parentId)
    {
        if (IsDeleted)
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        if (parentId == Id)
            return DomainResult.Failure(CategoryErrors.CannotBeOwnParent);

        if (ParentId == parentId)
            return DomainResult.Success();

        ParentId = parentId;

        return DomainResult.Success();
    }

    public DomainResult Activate()
    {
        if (IsDeleted)
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        if (Status == CategoryStatus.Active)
            return DomainResult.Success();

        Status = CategoryStatus.Active;

        AddDomainEvent(new CategoryActivated(Id));

        return DomainResult.Success();
    }

    public DomainResult Deactivate()
    {
        if (IsDeleted)
            return DomainResult.Failure(CategoryErrors.DeletedCategoryCannotBeModified);

        if (Status == CategoryStatus.Inactive)
            return DomainResult.Success();

        Status = CategoryStatus.Inactive;

        AddDomainEvent(new CategoryDeactivated(Id));

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
