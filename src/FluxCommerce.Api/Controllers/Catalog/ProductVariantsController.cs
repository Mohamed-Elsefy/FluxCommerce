using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductVariant;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductVariants;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/products/{productId}/variants")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class ProductVariantsController : ApiControllerBase
{
    private readonly ICommandHandler<CreateProductVariantCommand, Result<Guid>> _createProductVariantHandler;
    private readonly ICommandHandler<CreateProductVariantsCommand, Result<IReadOnlyCollection<Guid>>> _createProductVariantsHandler;
    private readonly ICommandHandler<UpdateProductVariantCommand, Result> _updateProductVariantHandler;
    private readonly ICommandHandler<DeleteProductVariantCommand, Result> _deleteProductVariantHandler;
    private readonly IQueryHandler<GetProductVariantQuery, Result<VariantResponse>> _getProductVariantHandler;
    private readonly IQueryHandler<ListProductVariantsQuery, Result<IReadOnlyCollection<VariantResponse>>> _listProductVariantsHandler;

    public ProductVariantsController(
        ICommandHandler<CreateProductVariantCommand, Result<Guid>> createProductVariantHandler,
        ICommandHandler<CreateProductVariantsCommand, Result<IReadOnlyCollection<Guid>>> createProductVariantsHandler,
        ICommandHandler<UpdateProductVariantCommand, Result> updateProductVariantHandler,
        ICommandHandler<DeleteProductVariantCommand, Result> deleteProductVariantHandler,
        IQueryHandler<GetProductVariantQuery, Result<VariantResponse>> getProductVariantHandler,
        IQueryHandler<ListProductVariantsQuery, Result<IReadOnlyCollection<VariantResponse>>> listProductVariantsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createProductVariantHandler = createProductVariantHandler;
        _createProductVariantsHandler = createProductVariantsHandler;
        _updateProductVariantHandler = updateProductVariantHandler;
        _deleteProductVariantHandler = deleteProductVariantHandler;
        _getProductVariantHandler = getProductVariantHandler;
        _listProductVariantsHandler = listProductVariantsHandler;
    }

    /// <summary>Creates a new variant for the specified product.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid productId, [FromBody] CreateProductVariantRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductVariantCommand(productId, request.Sku, request.Name, request.UnitPriceAmount, request.Currency);
        var result = await _createProductVariantHandler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { productId, variantId = result.Value }, ApiResponse<Guid>.Create(result.Value))
            : Created<Guid>(result);
    }

    /// <summary>Creates multiple variants for the specified product.</summary>
    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch(Guid productId, [FromBody] CreateProductVariantsRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductVariantsCommand(
            productId,
            request.Variants?.Select(v => new CreateProductVariantItemCommand(v.Sku, v.Name, v.UnitPriceAmount, v.Currency)).ToList() ?? []);

        var result = await _createProductVariantsHandler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(List), new { productId }, ApiResponse<IReadOnlyCollection<Guid>>.Create(result.Value))
            : Created<IReadOnlyCollection<Guid>>(result);
    }

    /// <summary>Updates an existing variant.</summary>
    [HttpPut("{variantId:guid}")]
    public async Task<IActionResult> Update(Guid productId, Guid variantId, [FromBody] UpdateProductVariantRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductVariantCommand(variantId, request.Sku, request.Name);
        return NoContent(await _updateProductVariantHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deletes a variant.</summary>
    [HttpDelete("{variantId:guid}")]
    public async Task<IActionResult> Delete(Guid productId, Guid variantId, CancellationToken cancellationToken)
    {
        var command = new DeleteProductVariantCommand(variantId);
        return NoContent(await _deleteProductVariantHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns a variant by its id.</summary>
    [HttpGet("{variantId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, Guid variantId, CancellationToken cancellationToken)
    {
        var query = new GetProductVariantQuery(variantId);
        return Success(await _getProductVariantHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists all non-deleted variants for the specified product.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken)
    {
        var query = new ListProductVariantsQuery(productId);
        return Success(await _listProductVariantsHandler.HandleAsync(query, cancellationToken));
    }
}
