using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductVariants;

public sealed class ListProductVariantsQueryHandler
    : IQueryHandler<ListProductVariantsQuery, Result<IReadOnlyCollection<VariantResponse>>>
{
    private readonly IVariantRepository _variantRepository;

    public ListProductVariantsQueryHandler(IVariantRepository variantRepository)
    {
        _variantRepository = variantRepository;
    }

    public async Task<Result<IReadOnlyCollection<VariantResponse>>> HandleAsync(
        ListProductVariantsQuery query,
        CancellationToken cancellationToken = default)
    {
        var variants = await _variantRepository.GetByProductIdAsync(query.ProductId, cancellationToken);

        return Result.Success(VariantResponseMapper.ToResponses(variants));
    }
}
