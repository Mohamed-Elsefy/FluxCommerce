using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.AssignProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.RemoveProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductAttributeValue;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductAttributeValues;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using FluxCommerce.Api.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/products/{productId}/attributes")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class ProductAttributeValuesController : ApiControllerBase
{
    private readonly ICommandHandler<AssignProductAttributeValueCommand, Result> _assignProductAttributeValueHandler;
    private readonly ICommandHandler<UpdateProductAttributeValueCommand, Result> _updateProductAttributeValueHandler;
    private readonly ICommandHandler<RemoveProductAttributeValueCommand, Result> _removeProductAttributeValueHandler;
    private readonly IQueryHandler<GetProductAttributeValueQuery, Result<ProductAttributeValueResponse>> _getProductAttributeValueHandler;
    private readonly IQueryHandler<ListProductAttributeValuesQuery, Result<IReadOnlyCollection<ProductAttributeValueResponse>>> _listProductAttributeValuesHandler;

    public ProductAttributeValuesController(
        ICommandHandler<AssignProductAttributeValueCommand, Result> assignProductAttributeValueHandler,
        ICommandHandler<UpdateProductAttributeValueCommand, Result> updateProductAttributeValueHandler,
        ICommandHandler<RemoveProductAttributeValueCommand, Result> removeProductAttributeValueHandler,
        IQueryHandler<GetProductAttributeValueQuery, Result<ProductAttributeValueResponse>> getProductAttributeValueHandler,
        IQueryHandler<ListProductAttributeValuesQuery, Result<IReadOnlyCollection<ProductAttributeValueResponse>>> listProductAttributeValuesHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _assignProductAttributeValueHandler = assignProductAttributeValueHandler;
        _updateProductAttributeValueHandler = updateProductAttributeValueHandler;
        _removeProductAttributeValueHandler = removeProductAttributeValueHandler;
        _getProductAttributeValueHandler = getProductAttributeValueHandler;
        _listProductAttributeValuesHandler = listProductAttributeValuesHandler;
    }

    /// <summary>Assigns an attribute value to the specified product.</summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
    public async Task<IActionResult> Assign(Guid productId, [FromBody] AssignProductAttributeValueRequest request, CancellationToken cancellationToken)
    {
        var command = new AssignProductAttributeValueCommand(productId, request.AttributeDefinitionId, request.Value);
        return NoContent(await _assignProductAttributeValueHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Updates an attribute value of the specified product.</summary>
    [HttpPut("{attributeDefinitionId:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
    public async Task<IActionResult> Update(Guid productId, Guid attributeDefinitionId, [FromBody] UpdateProductAttributeValueRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProductAttributeValueCommand(productId, attributeDefinitionId, request.Value);
        return NoContent(await _updateProductAttributeValueHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Removes an attribute value from the specified product.</summary>
    [HttpDelete("{attributeDefinitionId:guid}")]
    [Authorize(Policy = AuthenticationConfiguration.AdminPolicy)]
    public async Task<IActionResult> Remove(Guid productId, Guid attributeDefinitionId, CancellationToken cancellationToken)
    {
        var command = new RemoveProductAttributeValueCommand(productId, attributeDefinitionId);
        return NoContent(await _removeProductAttributeValueHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns an attribute value of the specified product.</summary>
    [HttpGet("{attributeDefinitionId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid productId, Guid attributeDefinitionId, CancellationToken cancellationToken)
    {
        var query = new GetProductAttributeValueQuery(productId, attributeDefinitionId);
        return Success(await _getProductAttributeValueHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists all attribute values for the specified product.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(Guid productId, CancellationToken cancellationToken)
    {
        var query = new ListProductAttributeValuesQuery(productId);
        return Success(await _listProductAttributeValuesHandler.HandleAsync(query, cancellationToken));
    }
}


