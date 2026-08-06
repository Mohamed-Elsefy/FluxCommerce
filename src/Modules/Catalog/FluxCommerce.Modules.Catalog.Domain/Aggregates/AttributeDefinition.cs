using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class AttributeDefinition : AggregateRoot<Guid>
{
    public const int NameMaxLength = 100;

    public const int DescriptionMaxLength = 500;

    public string Name { get; private set; }

    public Slug Slug { get; private set; }

    public string? Description { get; private set; }

    public AttributeDataType DataType { get; private set; }

    public bool IsRequired { get; private set; }

    public bool IsFilterable { get; private set; }

    public bool IsSearchable { get; private set; }

    public int SortOrder { get; private set; }

    public Guid? CategoryId { get; private set; }

    public AttributeStatus Status { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime? DeletedAtUtc { get; private set; }

    private AttributeDefinition(
        Guid id,
        string name,
        Slug slug,
        string? description,
        AttributeDataType dataType,
        bool isRequired,
        bool isFilterable,
        bool isSearchable,
        int sortOrder,
        Guid? categoryId,
        DateTime createdAtUtc)
        : base(id)
    {
        Name = ValidateName(name, nameof(name), NameMaxLength);
        Slug = slug;
        Description = NormalizeDescription(description, nameof(description));
        DataType = dataType;
        IsRequired = isRequired;
        IsFilterable = isFilterable;
        IsSearchable = isSearchable;
        SortOrder = sortOrder;
        CategoryId = categoryId;
        Status = AttributeStatus.Active;
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        DeletedAtUtc = null;
    }

    #pragma warning disable CS8618
    private AttributeDefinition() { }
    #pragma warning restore CS8618

    public static AttributeDefinition Create(
        Guid id,
        string name,
        Slug slug,
        AttributeDataType dataType,
        DateTime utcNow,
        string? description = null,
        bool isRequired = false,
        bool isFilterable = false,
        bool isSearchable = false,
        int sortOrder = 0,
        Guid? categoryId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An attribute definition id must be provided.", nameof(id));

        var attributeDefinition = new AttributeDefinition(
            id,
            name,
            slug,
            description,
            dataType,
            isRequired,
            isFilterable,
            isSearchable,
            sortOrder,
            categoryId,
            utcNow);

        attributeDefinition.AddDomainEvent(new AttributeDefinitionCreated(id));

        return attributeDefinition;
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
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        var normalizedName = ValidateName(name, nameof(name), NameMaxLength);

        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return DomainResult.Success();

        Name = normalizedName;

        return DomainResult.Success();
    }

    public DomainResult ChangeSlug(Slug slug)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (Slug == slug)
            return DomainResult.Success();

        Slug = slug;

        return DomainResult.Success();
    }

    public DomainResult ChangeDescription(string? description)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        Description = NormalizeDescription(description, nameof(description));

        return DomainResult.Success();
    }

    public DomainResult ChangeDataType(AttributeDataType dataType)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (DataType == dataType)
            return DomainResult.Success();

        DataType = dataType;

        return DomainResult.Success();
    }

    public DomainResult ChangeRequired(bool isRequired)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (IsRequired == isRequired)
            return DomainResult.Success();

        IsRequired = isRequired;

        return DomainResult.Success();
    }

    public DomainResult ChangeFilterable(bool isFilterable)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (IsFilterable == isFilterable)
            return DomainResult.Success();

        IsFilterable = isFilterable;

        return DomainResult.Success();
    }

    public DomainResult ChangeSearchable(bool isSearchable)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (IsSearchable == isSearchable)
            return DomainResult.Success();

        IsSearchable = isSearchable;

        return DomainResult.Success();
    }

    public DomainResult ChangeSortOrder(int sortOrder)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (SortOrder == sortOrder)
            return DomainResult.Success();

        SortOrder = sortOrder;

        return DomainResult.Success();
    }

    public DomainResult ChangeCategory(Guid? categoryId)
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (CategoryId == categoryId)
            return DomainResult.Success();

        CategoryId = categoryId;

        return DomainResult.Success();
    }

    public DomainResult Activate()
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (Status == AttributeStatus.Active)
            return DomainResult.Success();

        Status = AttributeStatus.Active;

        AddDomainEvent(new AttributeDefinitionActivated(Id));

        return DomainResult.Success();
    }

    public DomainResult Deactivate()
    {
        if (IsDeleted)
            return DomainResult.Failure(AttributeDefinitionErrors.DeletedAttributeDefinitionCannotBeModified);

        if (Status == AttributeStatus.Inactive)
            return DomainResult.Success();

        Status = AttributeStatus.Inactive;

        AddDomainEvent(new AttributeDefinitionDeactivated(Id));

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
