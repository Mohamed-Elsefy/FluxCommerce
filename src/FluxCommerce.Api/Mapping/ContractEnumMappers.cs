using ContractsAttributeDataType = FluxCommerce.Modules.Catalog.Contracts.Enums.AttributeDataType;
using ContractsCatalogProductSortBy = FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortBy;
using ContractsCatalogProductSortDirection = FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortDirection;
using ContractsCategoryStatus = FluxCommerce.Modules.Catalog.Contracts.Enums.CategoryStatus;
using ContractsProductStatus = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductStatus;
using ContractsProductVisibility = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductVisibility;
using ContractsVariantStatus = FluxCommerce.Modules.Catalog.Contracts.Enums.VariantStatus;
using ContractsInventoryReservationStatus = FluxCommerce.Modules.Inventory.Contracts.Enums.InventoryReservationStatus;
using DomainAttributeDataType = FluxCommerce.Modules.Catalog.Domain.Enums.AttributeDataType;
using DomainCatalogProductSortBy = FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortBy;
using DomainCatalogProductSortDirection = FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortDirection;
using DomainCategoryStatus = FluxCommerce.Modules.Catalog.Domain.Enums.CategoryStatus;
using DomainInventoryReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;
using DomainProductStatus = FluxCommerce.Modules.Catalog.Domain.Enums.ProductStatus;
using DomainProductVisibility = FluxCommerce.Modules.Catalog.Domain.Enums.ProductVisibility;
using DomainVariantStatus = FluxCommerce.Modules.Catalog.Domain.Enums.VariantStatus;

namespace FluxCommerce.Api.Mapping;

/// <summary>
/// Host-side contract/domain enum translation. The switches live here instead of
/// scattered across controllers so controllers only pass values through; the wire
/// values are unchanged (each pair mirrors by name).
/// </summary>
public static class ContractEnumMappers
{
    public static DomainProductVisibility ToDomainVisibility(ContractsProductVisibility visibility) =>
        visibility switch
        {
            ContractsProductVisibility.Visible => DomainProductVisibility.Visible,
            ContractsProductVisibility.Hidden => DomainProductVisibility.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };

    public static DomainProductVisibility? ToDomainVisibility(ContractsProductVisibility? visibility) =>
        visibility switch
        {
            null => null,
            _ => ToDomainVisibility(visibility.Value)
        };

    public static DomainProductStatus? ToDomainProductStatus(ContractsProductStatus? status) =>
        status switch
        {
            null => null,
            ContractsProductStatus.Draft => DomainProductStatus.Draft,
            ContractsProductStatus.Active => DomainProductStatus.Active,
            ContractsProductStatus.Inactive => DomainProductStatus.Inactive,
            ContractsProductStatus.Archived => DomainProductStatus.Archived,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    public static DomainCatalogProductSortBy? ToDomainSortBy(ContractsCatalogProductSortBy? sortBy) =>
        sortBy switch
        {
            null => null,
            ContractsCatalogProductSortBy.Name => DomainCatalogProductSortBy.Name,
            ContractsCatalogProductSortBy.CreatedAtUtc => DomainCatalogProductSortBy.CreatedAtUtc,
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null)
        };

    public static DomainCatalogProductSortDirection? ToDomainSortDirection(
        ContractsCatalogProductSortDirection? sortDirection) =>
        sortDirection switch
        {
            null => null,
            ContractsCatalogProductSortDirection.Ascending => DomainCatalogProductSortDirection.Ascending,
            ContractsCatalogProductSortDirection.Descending => DomainCatalogProductSortDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(sortDirection), sortDirection, null)
        };

    public static DomainCategoryStatus? ToDomainCategoryStatus(ContractsCategoryStatus? status) =>
        status switch
        {
            null => null,
            ContractsCategoryStatus.Active => DomainCategoryStatus.Active,
            ContractsCategoryStatus.Inactive => DomainCategoryStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    public static DomainVariantStatus? ToDomainVariantStatus(ContractsVariantStatus? status) =>
        status switch
        {
            null => null,
            ContractsVariantStatus.Active => DomainVariantStatus.Active,
            ContractsVariantStatus.Inactive => DomainVariantStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    public static DomainAttributeDataType ToDomainAttributeDataType(ContractsAttributeDataType dataType) =>
        dataType switch
        {
            ContractsAttributeDataType.Text => DomainAttributeDataType.Text,
            ContractsAttributeDataType.Number => DomainAttributeDataType.Number,
            ContractsAttributeDataType.Decimal => DomainAttributeDataType.Decimal,
            ContractsAttributeDataType.Boolean => DomainAttributeDataType.Boolean,
            ContractsAttributeDataType.Date => DomainAttributeDataType.Date,
            ContractsAttributeDataType.DateTime => DomainAttributeDataType.DateTime,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null)
        };

    public static ContractsInventoryReservationStatus ToContractReservationStatus(
        DomainInventoryReservationStatus status) =>
        status switch
        {
            DomainInventoryReservationStatus.Pending => ContractsInventoryReservationStatus.Pending,
            DomainInventoryReservationStatus.Committed => ContractsInventoryReservationStatus.Committed,
            DomainInventoryReservationStatus.Released => ContractsInventoryReservationStatus.Released,
            DomainInventoryReservationStatus.Expired => ContractsInventoryReservationStatus.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
