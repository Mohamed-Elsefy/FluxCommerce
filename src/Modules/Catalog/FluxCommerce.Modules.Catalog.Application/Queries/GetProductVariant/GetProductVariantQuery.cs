using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetProductVariant;

public sealed record GetProductVariantQuery(Guid Id) : IQuery<Result<VariantResponse>>;
