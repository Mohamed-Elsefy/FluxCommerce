using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.ArchiveProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProduct;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;
using FluxCommerce.Modules.Catalog.Application.Queries.SearchCatalogProducts;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using ContractsCatalogProductSortBy = FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortBy;
using ContractsCatalogProductSortDirection = FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortDirection;
using ContractsProductStatus = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductStatus;
using ContractsProductVisibility = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductVisibility;
using DomainCatalogProductSortBy = FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortBy;
using DomainCatalogProductSortDirection = FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortDirection;
using DomainProductStatus = FluxCommerce.Modules.Catalog.Domain.Enums.ProductStatus;
using DomainProductVisibility = FluxCommerce.Modules.Catalog.Domain.Enums.ProductVisibility;

using FluxCommerce.Api.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/products")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class ProductsController : ApiControllerBase
{
    private readonly ICommandHandler<CreateProductCommand, Result<Guid>> _createProductHandler;
    private readonly ICommandHandler<UpdateProductCommand, Result> _updateProductHandler;
    private readonly ICommandHandler<ActivateProductCommand, Result> _activateProductHandler;
    private readonly ICommandHandler<DeactivateProductCommand, Result> _deactivateProductHandler;
    private readonly ICommandHandler<ArchiveProductCommand, Result> _archiveProductHandler;
    private readonly ICommandHandler<SoftDeleteProductCommand, Result> _softDeleteProductHandler;
    private readonly ICommandHandler<RestoreProductCommand, Result> _restoreProductHandler;
    private readonly IQueryHandler<GetProductQuery, Result<ProductResponse>> _getProductHandler;
    private readonly IQueryHandler<ListProductsQuery, Result<PagedResult<ProductResponse>>> _listProductsHandler;
    private readonly IQueryHandler<SearchCatalogProductsQuery, Result<PagedResult<CatalogProductListItem>>> _searchCatalogProductsHandler;

    public ProductsController(
        ICommandHandler<CreateProductCommand, Result<Guid>> createProductHandler,
        ICommandHandler<UpdateProductCommand, Result> updateProductHandler,
        ICommandHandler<ActivateProductCommand, Result> activateProductHandler,
        ICommandHandler<DeactivateProductCommand, Result> deactivateProductHandler,
        ICommandHandler<ArchiveProductCommand, Result> archiveProductHandler,
        ICommandHandler<SoftDeleteProductCommand, Result> softDeleteProductHandler,
        ICommandHandler<RestoreProductCommand, Result> restoreProductHandler,
        IQueryHandler<GetProductQuery, Result<ProductResponse>> getProductHandler,
        IQueryHandler<ListProductsQuery, Result<PagedResult<ProductResponse>>> listProductsHandler,
        IQueryHandler<SearchCatalogProductsQuery, Result<PagedResult<CatalogProductListItem>>> searchCatalogProductsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createProductHandler = createProductHandler;
        _updateProductHandler = updateProductHandler;
        _activateProductHandler = activateProductHandler;
        _deactivateProductHandler = deactivateProductHandler;
        _archiveProductHandler = archiveProductHandler;
        _softDeleteProductHandler = softDeleteProductHandler;
        _restoreProductHandler = restoreProductHandler;
        _getProductHandler = getProductHandler;
        _listProductsHandler = listProductsHandler;
        _searchCatalogProductsHandler = searchCatalogProductsHandler;
    }

    /// <summary>Creates a new product.</summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(
            request.Name,
            request.Slug,
            request.Description,
            request.CategoryId,
            ToDomainVisibility(request.Visibility),
            ToSeoCommand(request.Seo));
        var result = await _createProductHandler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, ApiResponse<Guid>.Create(result.Value))
            : Created<Guid>(result);
    }

    /// <summary>Updates an existing product.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductCommand(
            id,
            request.Name,
            request.Slug,
            request.Description,
            request.CategoryId,
            ToDomainVisibility(request.Visibility),
            ToSeoCommand(request.Seo));
        return NoContent(await _updateProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Activates a product.</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var command = new ActivateProductCommand(id);
        return NoContent(await _activateProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deactivates a product.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeactivateProductCommand(id);
        return NoContent(await _deactivateProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Archives a product.</summary>
    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var command = new ArchiveProductCommand(id);
        return NoContent(await _archiveProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Soft deletes a product.</summary>
    [HttpPost("{id:guid}/soft-delete")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        var command = new SoftDeleteProductCommand(id);
        return NoContent(await _softDeleteProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Restores a soft-deleted product.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var command = new RestoreProductCommand(id);
        return NoContent(await _restoreProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns a product by its id.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetProductQuery(id);
        return Success(await _getProductHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists non-deleted products with pagination, optional filters and sorting.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? searchTerm,
        [FromQuery] ContractsProductStatus? status,
        [FromQuery] ContractsProductVisibility? visibility,
        [FromQuery] Guid? categoryId,
        [FromQuery] ContractsCatalogProductSortBy? sortBy,
        [FromQuery] ContractsCatalogProductSortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new ListProductsQuery(
            pageNumber,
            pageSize,
            searchTerm,
            ToDomainStatus(status),
            ToDomainVisibility(visibility),
            categoryId,
            ToDomainSortBy(sortBy),
            ToDomainSortDirection(sortDirection));
        return Success(await _listProductsHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>
    /// Searches the public catalog combining search, filtering, sorting and pagination.
    /// </summary>
    [HttpGet("~/catalog")]
    [AllowAnonymous]
    public async Task<IActionResult> Search(
        [FromQuery] string? searchTerm,
        [FromQuery] Guid? categoryId,
        [FromQuery] ContractsProductStatus? status,
        [FromQuery] ContractsProductVisibility? visibility,
        [FromQuery] ContractsCatalogProductSortBy? sortBy,
        [FromQuery] ContractsCatalogProductSortDirection? sortDirection,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = new SearchCatalogProductsQuery(
            searchTerm ?? string.Empty,
            BuildFilter(categoryId, status, visibility),
            ToDomainSortBy(sortBy),
            ToDomainSortDirection(sortDirection),
            pageNumber,
            pageSize);

        return Success(await _searchCatalogProductsHandler.HandleAsync(query, cancellationToken));
    }

    private static CatalogProductFilter? BuildFilter(
        Guid? categoryId,
        ContractsProductStatus? status,
        ContractsProductVisibility? visibility)
    {
        if (categoryId is null && status is null && visibility is null)
            return null;

        return new CatalogProductFilter(categoryId, ToDomainStatus(status), ToDomainVisibility(visibility));
    }

    private static DomainProductVisibility ToDomainVisibility(ContractsProductVisibility visibility) =>
        visibility switch
        {
            ContractsProductVisibility.Visible => DomainProductVisibility.Visible,
            ContractsProductVisibility.Hidden => DomainProductVisibility.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };

    private static DomainProductVisibility? ToDomainVisibility(ContractsProductVisibility? visibility) =>
        visibility switch
        {
            null => null,
            ContractsProductVisibility.Visible => DomainProductVisibility.Visible,
            ContractsProductVisibility.Hidden => DomainProductVisibility.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };

    private static DomainProductStatus? ToDomainStatus(ContractsProductStatus? status) =>
        status switch
        {
            null => null,
            ContractsProductStatus.Draft => DomainProductStatus.Draft,
            ContractsProductStatus.Active => DomainProductStatus.Active,
            ContractsProductStatus.Inactive => DomainProductStatus.Inactive,
            ContractsProductStatus.Archived => DomainProductStatus.Archived,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static DomainCatalogProductSortBy? ToDomainSortBy(ContractsCatalogProductSortBy? sortBy) =>
        sortBy switch
        {
            null => null,
            ContractsCatalogProductSortBy.Name => DomainCatalogProductSortBy.Name,
            ContractsCatalogProductSortBy.CreatedAtUtc => DomainCatalogProductSortBy.CreatedAtUtc,
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null)
        };

    private static DomainCatalogProductSortDirection? ToDomainSortDirection(
        ContractsCatalogProductSortDirection? sortDirection) =>
        sortDirection switch
        {
            null => null,
            ContractsCatalogProductSortDirection.Ascending => DomainCatalogProductSortDirection.Ascending,
            ContractsCatalogProductSortDirection.Descending => DomainCatalogProductSortDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(sortDirection), sortDirection, null)
        };

    private static SeoMetadataCommand? ToSeoCommand(SeoRequest? seo) =>
        seo is null ? null : new SeoMetadataCommand(seo.Title, seo.Description, seo.Keywords);
}
