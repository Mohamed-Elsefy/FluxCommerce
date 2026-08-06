using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductVariants;

public sealed record ListProductVariantsQuery(Guid ProductId) : IQuery<Result<IReadOnlyCollection<VariantResponse>>>;
