using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Events;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Domain.Aggregates;

public sealed class Variant : AggregateRoot<Guid>
{
    public const int NameMaxLength = 200;

    public Guid ProductId { get; private set; }

    public Sku Sku { get; private set; }

    public string Name { get; private set; }

    public VariantStatus Status { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime? DeletedAtUtc { get; private set; }

    private Variant(Guid id, Guid productId, Sku sku, string name, DateTime createdAtUtc)
        : base(id)
    {
        ProductId = productId;
        Sku = sku;
        Name = ValidateName(name, nameof(name), NameMaxLength);
        Status = VariantStatus.Active;
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        DeletedAtUtc = null;
    }

    #pragma warning disable CS8618
    private Variant() { }
    #pragma warning restore CS8618

    public static Variant Create(Guid id, Guid productId, Sku sku, string name, DateTime utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A variant id must be provided.", nameof(id));

        if (productId == Guid.Empty)
            throw new ArgumentException("A product id must be provided.", nameof(productId));

        var variant = new Variant(id, productId, sku, name, utcNow);

        variant.AddDomainEvent(new VariantCreated(id));

        return variant;
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

    public DomainResult Rename(string name)
    {
        if (IsDeleted)
            return DomainResult.Failure(VariantErrors.DeletedVariantCannotBeModified);

        var normalizedName = ValidateName(name, nameof(name), NameMaxLength);

        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return DomainResult.Success();

        Name = normalizedName;

        return DomainResult.Success();
    }

    public DomainResult ChangeSku(Sku sku)
    {
        if (IsDeleted)
            return DomainResult.Failure(VariantErrors.DeletedVariantCannotBeModified);

        if (Sku == sku)
            return DomainResult.Success();

        Sku = sku;

        AddDomainEvent(new VariantSkuChanged(Id, sku.Value));

        return DomainResult.Success();
    }

    public DomainResult Activate()
    {
        if (IsDeleted)
            return DomainResult.Failure(VariantErrors.DeletedVariantCannotBeModified);

        if (Status == VariantStatus.Active)
            return DomainResult.Success();

        Status = VariantStatus.Active;

        AddDomainEvent(new VariantActivated(Id));

        return DomainResult.Success();
    }

    public DomainResult Deactivate()
    {
        if (IsDeleted)
            return DomainResult.Failure(VariantErrors.DeletedVariantCannotBeModified);

        if (Status == VariantStatus.Inactive)
            return DomainResult.Success();

        Status = VariantStatus.Inactive;

        AddDomainEvent(new VariantDeactivated(Id));

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
