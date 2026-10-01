using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductSpecification;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductSpecifications;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using FluxCommerce.Api.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/products/{productId}/specifications")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class ProductSpecificationsController : ApiControllerBase
{
    private readonly ICommandHandler<CreateProductSpecificationCommand, Result> _createProductSpecificationHandler;
    private readonly ICommandHandler<UpdateProductSpecificationCommand, Result> _updateProductSpecificationHandler;
    private readonly ICommandHandler<DeleteProductSpecificationCommand, Result> _deleteProductSpecificationHandler;
    private readonly IQueryHandler<GetProductSpecificationQuery, Result<ProductSpecificationResponse>> _getProductSpecificationHandler;
    private readonly IQueryHandler<ListProductSpecificationsQuery, Result<IReadOnlyCollection<ProductSpecificationResponse>>> _listProductSpecificationsHandler;

    public ProductSpecificationsController(
        ICommandHandler<CreateProductSpecificationCommand, Result> createProductSpecificationHandler,
        ICommandHandler<UpdateProductSpecificationCommand, Result> updateProductSpecificationHandler,
        ICommandHandler<DeleteProductSpecificationCommand, Result> deleteProductSpecificationHandler,
        IQueryHandler<GetProductSpecificationQuery, Result<ProductSpecificationResponse>> getProductSpecificationHandler,
        IQueryHandler<ListProductSpecificationsQuery, Result<IReadOnlyCollection<ProductSpecificationResponse>>> listProductSpecificationsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createProductSpecificationHandler = createProductSpecificationHandler;
        _updateProductSpecificationHandler = updateProductSpecificationHandler;
        _deleteProductSpecificationHandler = deleteProductSpecificationHandler;
        _getProductSpecificationHandler = getProductSpecificationHandler;
        _listProductSpecificationsHandler = listProductSpecificationsHandler;
    }

    /// <summary>Creates a new specification for the specified product.</summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Create(Guid productId, [FromBody] CreateProductSpecificationRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductSpecificationCommand(productId, request.Name, request.Value);
        return NoContent(await _createProductSpecificationHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Updates an existing specification of the specified product.</summary>
    [HttpPut("{specificationId:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Update(Guid productId, Guid specificationId, [FromBody] UpdateProductSpecificationRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductSpecificationCommand(productId, specificationId, request.Name, request.Value);
        return NoContent(await _updateProductSpecificationHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deletes a specification of the specified product.</summary>
    [HttpDelete("{specificationId:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.CatalogManagePolicy)]
    public async Task<IActionResult> Delete(Guid productId, Guid specificationId, CancellationToken cancellationToken)
    {
        var command = new DeleteProductSpecificationCommand(productId, specificationId);
        return NoContent(await _deleteProductSpecificationHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns a specification of the specified product.</summary>
    [HttpGet("{specificationId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, Guid specificationId, CancellationToken cancellationToken)
    {
        var query = new GetProductSpecificationQuery(productId, specificationId);
        return Success(await _getProductSpecificationHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists all specifications for the specified product.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken)
    {
        var query = new ListProductSpecificationsQuery(productId);
        return Success(await _listProductSpecificationsHandler.HandleAsync(query, cancellationToken));
    }
}


