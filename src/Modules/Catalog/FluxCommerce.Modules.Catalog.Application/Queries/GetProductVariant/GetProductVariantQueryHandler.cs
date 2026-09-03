using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Mappings;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductVariant;

public sealed class GetProductVariantQueryHandler
    : IQueryHandler<GetProductVariantQuery, Result<VariantResponse>>
{
    private readonly IVariantRepository _variantRepository;

    public GetProductVariantQueryHandler(IVariantRepository variantRepository)
    {
        _variantRepository = variantRepository;
    }

    public async Task<Result<VariantResponse>> HandleAsync(
        GetProductVariantQuery query,
        CancellationToken cancellationToken = default)
    {
        var variant = await _variantRepository.GetByIdAsync(query.Id, cancellationToken);

        if (variant is null)
            return Result.Failure<VariantResponse>(
                Error.NotFound("Catalog.VariantNotFound", "The variant was not found."));

        return Result.Success(VariantResponseMapper.ToResponse(variant));
    }
}
