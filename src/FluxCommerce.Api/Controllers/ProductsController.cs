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
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using ContractsProductVisibility = FluxCommerce.Modules.Catalog.Contracts.Enums.ProductVisibility;
using DomainProductVisibility = FluxCommerce.Modules.Catalog.Domain.Enums.ProductVisibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers;

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
    private readonly IQueryHandler<ListProductsQuery, Result<IReadOnlyCollection<ProductResponse>>> _listProductsHandler;

    public ProductsController(
        ICommandHandler<CreateProductCommand, Result<Guid>> createProductHandler,
        ICommandHandler<UpdateProductCommand, Result> updateProductHandler,
        ICommandHandler<ActivateProductCommand, Result> activateProductHandler,
        ICommandHandler<DeactivateProductCommand, Result> deactivateProductHandler,
        ICommandHandler<ArchiveProductCommand, Result> archiveProductHandler,
        ICommandHandler<SoftDeleteProductCommand, Result> softDeleteProductHandler,
        ICommandHandler<RestoreProductCommand, Result> restoreProductHandler,
        IQueryHandler<GetProductQuery, Result<ProductResponse>> getProductHandler,
        IQueryHandler<ListProductsQuery, Result<IReadOnlyCollection<ProductResponse>>> listProductsHandler,
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
    }

    /// <summary>Creates a new product.</summary>
    [HttpPost]
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
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var command = new ActivateProductCommand(id);
        return NoContent(await _activateProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deactivates a product.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeactivateProductCommand(id);
        return NoContent(await _deactivateProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Archives a product.</summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var command = new ArchiveProductCommand(id);
        return NoContent(await _archiveProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Soft deletes a product.</summary>
    [HttpPost("{id:guid}/soft-delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        var command = new SoftDeleteProductCommand(id);
        return NoContent(await _softDeleteProductHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Restores a soft-deleted product.</summary>
    [HttpPost("{id:guid}/restore")]
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

    /// <summary>Lists all non-deleted products.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var query = new ListProductsQuery();
        return Success(await _listProductsHandler.HandleAsync(query, cancellationToken));
    }

    private static DomainProductVisibility ToDomainVisibility(ContractsProductVisibility visibility) =>
        visibility switch
        {
            ContractsProductVisibility.Visible => DomainProductVisibility.Visible,
            ContractsProductVisibility.Hidden => DomainProductVisibility.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };

    private static SeoMetadataCommand? ToSeoCommand(SeoRequest? seo) =>
        seo is null ? null : new SeoMetadataCommand(seo.Title, seo.Description, seo.Keywords);
}
