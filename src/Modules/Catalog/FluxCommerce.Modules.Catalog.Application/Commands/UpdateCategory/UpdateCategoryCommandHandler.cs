using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;

public sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand, Result>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public UpdateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        UpdateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new UpdateCategoryCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var category = await _categoryRepository.GetByIdAsync(command.Id, cancellationToken);

        if (category is null)
            return Result.Failure(
                Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

        Slug slug;
        try
        {
            slug = new Slug(command.Slug!);
        }
        catch (ArgumentException)
        {
            return Result.Failure(
                Error.Validation(CatalogErrors.InvalidSlug.Code, CatalogErrors.InvalidSlug.Description));
        }

        var name = command.Name!.Trim();

        if (!string.Equals(category.Name, name, StringComparison.Ordinal) &&
            await _categoryRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateCategoryName", "A category with this name already exists."));

        if (category.Slug != slug && await _categoryRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateCategorySlug", "A category with this slug already exists."));

        if (command.ParentId is { } parentId)
        {
            var parent = await _categoryRepository.GetByIdAsync(parentId, cancellationToken);

            if (parent is null)
                return Result.Failure(
                    Error.NotFound("Catalog.ParentCategoryNotFound", "The parent category was not found."));

            if (parent.IsDeleted)
                return Result.Failure(
                    Error.Conflict("Catalog.ParentCategoryDeleted", "The parent category is deleted and cannot be used."));
        }

        var renameResult = category.Rename(name);

        if (renameResult.IsFailure)
            return Result.Failure(ToApplicationError(renameResult));

        var descriptionResult = category.ChangeDescription(command.Description);

        if (descriptionResult.IsFailure)
            return Result.Failure(ToApplicationError(descriptionResult));

        var slugResult = category.ChangeSlug(slug);

        if (slugResult.IsFailure)
            return Result.Failure(ToApplicationError(slugResult));

        var parentResult = category.ChangeParent(command.ParentId);

        if (parentResult.IsFailure)
            return Result.Failure(ToApplicationError(parentResult));

        await _categoryRepository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
