using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.AddProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.ReorderProductImages;
using FluxCommerce.Modules.Catalog.Application.Commands.SetPrimaryProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductImage;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductImages;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers;

[ApiController]
[Route("catalog/products/{productId}/images")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class ProductImagesController : ApiControllerBase
{
    private readonly ICommandHandler<AddProductImageCommand, Result> _addProductImageHandler;
    private readonly ICommandHandler<UpdateProductImageCommand, Result> _updateProductImageHandler;
    private readonly ICommandHandler<DeleteProductImageCommand, Result> _deleteProductImageHandler;
    private readonly ICommandHandler<SetPrimaryProductImageCommand, Result> _setPrimaryProductImageHandler;
    private readonly ICommandHandler<ReorderProductImagesCommand, Result> _reorderProductImagesHandler;
    private readonly IQueryHandler<GetProductImageQuery, Result<ProductImageResponse>> _getProductImageHandler;
    private readonly IQueryHandler<ListProductImagesQuery, Result<IReadOnlyCollection<ProductImageResponse>>> _listProductImagesHandler;

    public ProductImagesController(
        ICommandHandler<AddProductImageCommand, Result> addProductImageHandler,
        ICommandHandler<UpdateProductImageCommand, Result> updateProductImageHandler,
        ICommandHandler<DeleteProductImageCommand, Result> deleteProductImageHandler,
        ICommandHandler<SetPrimaryProductImageCommand, Result> setPrimaryProductImageHandler,
        ICommandHandler<ReorderProductImagesCommand, Result> reorderProductImagesHandler,
        IQueryHandler<GetProductImageQuery, Result<ProductImageResponse>> getProductImageHandler,
        IQueryHandler<ListProductImagesQuery, Result<IReadOnlyCollection<ProductImageResponse>>> listProductImagesHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _addProductImageHandler = addProductImageHandler;
        _updateProductImageHandler = updateProductImageHandler;
        _deleteProductImageHandler = deleteProductImageHandler;
        _setPrimaryProductImageHandler = setPrimaryProductImageHandler;
        _reorderProductImagesHandler = reorderProductImagesHandler;
        _getProductImageHandler = getProductImageHandler;
        _listProductImagesHandler = listProductImagesHandler;
    }

    /// <summary>Adds a new image to the specified product.</summary>
    [HttpPost]
    public async Task<IActionResult> Add(Guid productId, [FromBody] AddProductImageRequest request, CancellationToken cancellationToken)
    {
        var command = new AddProductImageCommand(
            productId,
            request.ImageUrl,
            request.AltText,
            request.IsPrimary,
            request.DisplayOrder,
            request.Metadata);

        return NoContent(await _addProductImageHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Updates the metadata of an existing product image.</summary>
    [HttpPut("{imageId:guid}")]
    public async Task<IActionResult> Update(Guid productId, Guid imageId, [FromBody] UpdateProductImageRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductImageCommand(
            productId,
            imageId,
            request.ImageUrl,
            request.AltText,
            request.DisplayOrder,
            request.Metadata);

        return NoContent(await _updateProductImageHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deletes a product image.</summary>
    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId, CancellationToken cancellationToken)
    {
        var command = new DeleteProductImageCommand(productId, imageId);
        return NoContent(await _deleteProductImageHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Sets the specified product image as the primary image.</summary>
    [HttpPost("{imageId:guid}/set-primary")]
    public async Task<IActionResult> SetPrimary(Guid productId, Guid imageId, CancellationToken cancellationToken)
    {
        var command = new SetPrimaryProductImageCommand(productId, imageId);
        return NoContent(await _setPrimaryProductImageHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Reorders the images of the specified product.</summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder(Guid productId, [FromBody] ReorderProductImagesRequest request, CancellationToken cancellationToken)
    {
        var command = new ReorderProductImagesCommand(productId, request.OrderedImageIds ?? []);
        return NoContent(await _reorderProductImagesHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns a product image by its id.</summary>
    [HttpGet("{imageId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, Guid imageId, CancellationToken cancellationToken)
    {
        var query = new GetProductImageQuery(productId, imageId);
        return Success(await _getProductImageHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists all images for the specified product.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken)
    {
        var query = new ListProductImagesQuery(productId);
        return Success(await _listProductImagesHandler.HandleAsync(query, cancellationToken));
    }
}
