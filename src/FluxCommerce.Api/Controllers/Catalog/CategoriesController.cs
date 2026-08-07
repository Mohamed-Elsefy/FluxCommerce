using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCategory;
using FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;
using FluxCommerce.Modules.Catalog.Contracts.Requests;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Catalog;

[ApiController]
[Route("catalog/categories")]
[Authorize]
[Tags(EndpointTags.Catalog)]
public sealed class CategoriesController : ApiControllerBase
{
    private readonly ICommandHandler<CreateCategoryCommand, Result<Guid>> _createCategoryHandler;
    private readonly ICommandHandler<UpdateCategoryCommand, Result> _updateCategoryHandler;
    private readonly ICommandHandler<ActivateCategoryCommand, Result> _activateCategoryHandler;
    private readonly ICommandHandler<DeactivateCategoryCommand, Result> _deactivateCategoryHandler;
    private readonly ICommandHandler<SoftDeleteCategoryCommand, Result> _softDeleteCategoryHandler;
    private readonly ICommandHandler<RestoreCategoryCommand, Result> _restoreCategoryHandler;
    private readonly IQueryHandler<GetCategoryQuery, Result<CategoryResponse>> _getCategoryHandler;
    private readonly IQueryHandler<ListCategoriesQuery, Result<PagedResult<CategoryResponse>>> _listCategoriesHandler;

    public CategoriesController(
        ICommandHandler<CreateCategoryCommand, Result<Guid>> createCategoryHandler,
        ICommandHandler<UpdateCategoryCommand, Result> updateCategoryHandler,
        ICommandHandler<ActivateCategoryCommand, Result> activateCategoryHandler,
        ICommandHandler<DeactivateCategoryCommand, Result> deactivateCategoryHandler,
        ICommandHandler<SoftDeleteCategoryCommand, Result> softDeleteCategoryHandler,
        ICommandHandler<RestoreCategoryCommand, Result> restoreCategoryHandler,
        IQueryHandler<GetCategoryQuery, Result<CategoryResponse>> getCategoryHandler,
        IQueryHandler<ListCategoriesQuery, Result<PagedResult<CategoryResponse>>> listCategoriesHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createCategoryHandler = createCategoryHandler;
        _updateCategoryHandler = updateCategoryHandler;
        _activateCategoryHandler = activateCategoryHandler;
        _deactivateCategoryHandler = deactivateCategoryHandler;
        _softDeleteCategoryHandler = softDeleteCategoryHandler;
        _restoreCategoryHandler = restoreCategoryHandler;
        _getCategoryHandler = getCategoryHandler;
        _listCategoriesHandler = listCategoriesHandler;
    }

    /// <summary>Creates a new category.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateCategoryCommand(request.Name, request.Slug, request.Description, request.ParentId);
        var result = await _createCategoryHandler.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, ApiResponse<Guid>.Create(result.Value))
            : Created<Guid>(result);
    }

    /// <summary>Updates an existing category.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateCategoryCommand(id, request.Name, request.Slug, request.Description, request.ParentId);
        return NoContent(await _updateCategoryHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Activates a category.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var command = new ActivateCategoryCommand(id);
        return NoContent(await _activateCategoryHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Deactivates a category.</summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeactivateCategoryCommand(id);
        return NoContent(await _deactivateCategoryHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Soft deletes a category.</summary>
    [HttpPost("{id:guid}/soft-delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        var command = new SoftDeleteCategoryCommand(id);
        return NoContent(await _softDeleteCategoryHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Restores a soft-deleted category.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        var command = new RestoreCategoryCommand(id);
        return NoContent(await _restoreCategoryHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns a category by its id.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetCategoryQuery(id);
        return Success(await _getCategoryHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>Lists non-deleted categories with pagination.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = new ListCategoriesQuery(pageNumber, pageSize);
        return Success(await _listCategoriesHandler.HandleAsync(query, cancellationToken));
    }
}
