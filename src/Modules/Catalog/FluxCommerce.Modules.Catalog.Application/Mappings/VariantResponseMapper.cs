using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class VariantResponseMapper
{
    public static VariantResponse ToResponse(Variant variant) =>
        new(
            variant.Id,
            variant.ProductId,
            variant.Sku.Value,
            variant.Name,
            variant.UnitPrice.Amount,
            variant.UnitPrice.Currency,
            ToStatus(variant.Status),
            variant.IsDeleted,
            variant.CreatedAtUtc,
            variant.DeletedAtUtc);

    public static IReadOnlyCollection<VariantResponse> ToResponses(
        IEnumerable<Variant> variants) =>
        variants.Select(ToResponse).ToList();

    private static VariantStatus ToStatus(Domain.Enums.VariantStatus status) =>
        status switch
        {
            Domain.Enums.VariantStatus.Active => VariantStatus.Active,
            Domain.Enums.VariantStatus.Inactive => VariantStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
