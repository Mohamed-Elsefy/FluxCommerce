using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateAttributeDefinition;

public sealed class UpdateAttributeDefinitionCommandHandler : ICommandHandler<UpdateAttributeDefinitionCommand, Result>
{
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public UpdateAttributeDefinitionCommandHandler(
        IAttributeDefinitionRepository attributeDefinitionRepository,
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        UpdateAttributeDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new UpdateAttributeDefinitionCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var attributeDefinition = await _attributeDefinitionRepository.GetByIdAsync(command.Id, cancellationToken);

        if (attributeDefinition is null)
            return Result.Failure(
                Error.NotFound("Catalog.AttributeDefinitionNotFound", "The attribute definition was not found."));

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

        if (!string.Equals(attributeDefinition.Name, name, StringComparison.Ordinal) &&
            await _attributeDefinitionRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateAttributeName", "An attribute definition with this name already exists."));

        if (attributeDefinition.Slug != slug && await _attributeDefinitionRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateAttributeSlug", "An attribute definition with this slug already exists."));

        if (command.CategoryId is { } categoryId)
        {
            var category = await _categoryRepository.GetByIdAsync(categoryId, cancellationToken);

            if (category is null)
                return Result.Failure(
                    Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

            if (category.IsDeleted)
                return Result.Failure(
                    Error.Conflict("Catalog.DeletedCategoryCannotBeUsed", "The category is deleted and cannot be used."));
        }

        var renameResult = attributeDefinition.Rename(name);

        if (renameResult.IsFailure)
            return Result.Failure(ToApplicationError(renameResult));

        var slugResult = attributeDefinition.ChangeSlug(slug);

        if (slugResult.IsFailure)
            return Result.Failure(ToApplicationError(slugResult));

        var descriptionResult = attributeDefinition.ChangeDescription(command.Description);

        if (descriptionResult.IsFailure)
            return Result.Failure(ToApplicationError(descriptionResult));

        var dataTypeResult = attributeDefinition.ChangeDataType(command.DataType);

        if (dataTypeResult.IsFailure)
            return Result.Failure(ToApplicationError(dataTypeResult));

        var requiredResult = attributeDefinition.ChangeRequired(command.IsRequired);

        if (requiredResult.IsFailure)
            return Result.Failure(ToApplicationError(requiredResult));

        var filterableResult = attributeDefinition.ChangeFilterable(command.IsFilterable);

        if (filterableResult.IsFailure)
            return Result.Failure(ToApplicationError(filterableResult));

        var searchableResult = attributeDefinition.ChangeSearchable(command.IsSearchable);

        if (searchableResult.IsFailure)
            return Result.Failure(ToApplicationError(searchableResult));

        var sortOrderResult = attributeDefinition.ChangeSortOrder(command.SortOrder);

        if (sortOrderResult.IsFailure)
            return Result.Failure(ToApplicationError(sortOrderResult));

        var categoryResult = attributeDefinition.ChangeCategory(command.CategoryId);

        if (categoryResult.IsFailure)
            return Result.Failure(ToApplicationError(categoryResult));

        await _attributeDefinitionRepository.UpdateAsync(attributeDefinition, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
