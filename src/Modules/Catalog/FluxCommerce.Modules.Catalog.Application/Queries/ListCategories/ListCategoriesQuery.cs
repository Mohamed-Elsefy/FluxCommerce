using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;

public sealed record ListCategoriesQuery() : IQuery<Result<IReadOnlyCollection<CategoryResponse>>>;
