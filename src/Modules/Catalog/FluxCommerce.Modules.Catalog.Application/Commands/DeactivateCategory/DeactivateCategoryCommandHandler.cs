using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;

public sealed class DeactivateCategoryCommandHandler : ICommandHandler<DeactivateCategoryCommand, Result>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public DeactivateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        DeactivateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new DeactivateCategoryCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var category = await _categoryRepository.GetByIdAsync(command.Id, cancellationToken);

        if (category is null)
            return Result.Failure(
                Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

        var result = category.Deactivate();

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _categoryRepository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
