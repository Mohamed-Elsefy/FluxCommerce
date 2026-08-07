using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Queries.GetAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Queries.ListAttributeDefinitions;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using ContractsAttributeDataType = FluxCommerce.Modules.Catalog.Contracts.Enums.AttributeDataType;
using DomainAttributeDataType = FluxCommerce.Modules.Catalog.Domain.Enums.AttributeDataType;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/attribute-definitions")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class AttributeDefinitionsController : ApiControllerBase
{
    private readonly ICommandHandler<CreateAttributeDefinitionCommand, Result<Guid>> _createAttributeDefinitionHandler;
    private readonly ICommandHandler<UpdateAttributeDefinitionCommand, Result> _updateAttributeDefinitionHandler;
    private readonly ICommandHandler<ActivateAttributeDefinitionCommand, Result> _activateAttributeDefinitionHandler;
    private readonly ICommandHandler<DeactivateAttributeDefinitionCommand, Result> _deactivateAttributeDefinitionHandler;
    private readonly ICommandHandler<SoftDeleteAttributeDefinitionCommand, Result> _softDeleteAttributeDefinitionHandler;
    private readonly ICommandHandler<RestoreAttributeDefinitionCommand, Result> _restoreAttributeDefinitionHandler;
    private readonly IQueryHandler<GetAttributeDefinitionQuery, Result<AttributeDefinitionResponse>> _getAttributeDefinitionHandler;
    private readonly IQueryHandler<ListAttributeDefinitionsQuery, Result<IReadOnlyCollection<AttributeDefinitionResponse>>> _listAttributeDefinitionsHandler;

    public AttributeDefinitionsController(
        ICommandHandler<CreateAttributeDefinitionCommand, Result<Guid>> createAttributeDefinitionHandler,
        ICommandHandler<UpdateAttributeDefinitionCommand, Result> updateAttributeDefinitionHandler,
        ICommandHandler<ActivateAttributeDefinitionCommand, Result> activateAttributeDefinitionHandler,
        ICommandHandler<DeactivateAttributeDefinitionCommand, Result> deactivateAttributeDefinitionHandler,
        ICommandHandler<SoftDeleteAttributeDefinitionCommand, Result> softDeleteAttributeDefinitionHandler,
        ICommandHandler<RestoreAttributeDefinitionCommand, Result> restoreAttributeDefinitionHandler,
        IQueryHandler<GetAttributeDefinitionQuery, Result<AttributeDefinitionResponse>> getAttributeDefinitionHandler,
        IQueryHandler<ListAttributeDefinitionsQuery, Result<IReadOnlyCollection<AttributeDefinitionResponse>>> listAttributeDefinitionsHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createAttributeDefinitionHandler = createAttributeDefinitionHandler;
        _updateAttributeDefinitionHandler = updateAttributeDefinitionHandler;
        _activateAttributeDefinitionHandler = activateAttributeDefinitionHandler;
        _deactivateAttributeDefinitionHandler = deactivateAttributeDefinitionHandler;
        _softDeleteAttributeDefinitionHandler = softDeleteAttributeDefinitionHandler;
        _restoreAttributeDefinitionHandler = restoreAttributeDefinitionHandler;
        _getAttributeDefinitionHandler = getAttributeDefinitionHandler;
        _listAttributeDefinitionsHandler = listAttributeDefinitionsHandler;
    }

    /// <summary>Creates a new attribute definition.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateAttributeDefinitionCommand(
            request.Name,
            request.Slug,
            request.Description,
            ToDomainDataType(request.DataType),
            request.IsRequired,
            request.IsFilterable,
            request.IsSearchable,
            request.SortOrder,
            request.CategoryId);
        var result = await _createAttributeDefinitionHandler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, ApiResponse<Guid>.Create(result.Value))
            : Created<Guid>(result);
    }

    /// <summary>Updates an existing attribute definition.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateAttributeDefinitionCommand(
            id,
            request.Name,
            request.Slug,
            request.Description,
            ToDomainDataType(request.DataType),
            request.IsRequired,
            request.IsFilterable,
            request.IsSearchable,
            request.SortOrder,
            request.CategoryId);
        return NoContent(await _updateAttributeDefinitionHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Activates an attribute definition.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var command = new ActivateAttributeDefinitionCommand(id);
        return NoContent(await _activateAttributeDefinitionHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deactivates an attribute definition.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeactivateAttributeDefinitionCommand(id);
        return NoContent(await _deactivateAttributeDefinitionHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Soft deletes an attribute definition.</summary>
    [HttpPost("{id:guid}/soft-delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        var command = new SoftDeleteAttributeDefinitionCommand(id);
        return NoContent(await _softDeleteAttributeDefinitionHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Restores a soft-deleted attribute definition.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var command = new RestoreAttributeDefinitionCommand(id);
        return NoContent(await _restoreAttributeDefinitionHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns an attribute definition by its id.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetAttributeDefinitionQuery(id);
        return Success(await _getAttributeDefinitionHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists all non-deleted attribute definitions.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var query = new ListAttributeDefinitionsQuery();
        return Success(await _listAttributeDefinitionsHandler.HandleAsync(query, cancellationToken));
    }

    private static DomainAttributeDataType ToDomainDataType(ContractsAttributeDataType dataType) =>
        dataType switch
        {
            ContractsAttributeDataType.Text => DomainAttributeDataType.Text,
            ContractsAttributeDataType.Number => DomainAttributeDataType.Number,
            ContractsAttributeDataType.Decimal => DomainAttributeDataType.Decimal,
            ContractsAttributeDataType.Boolean => DomainAttributeDataType.Boolean,
            ContractsAttributeDataType.Date => DomainAttributeDataType.Date,
            ContractsAttributeDataType.DateTime => DomainAttributeDataType.DateTime,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null)
        };
}
