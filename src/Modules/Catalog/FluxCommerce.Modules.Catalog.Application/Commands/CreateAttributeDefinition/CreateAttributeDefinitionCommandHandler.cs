using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateAttributeDefinition;

public sealed class CreateAttributeDefinitionCommandHandler : ICommandHandler<CreateAttributeDefinitionCommand, Result<Guid>>
{
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateAttributeDefinitionCommandHandler(
        IAttributeDefinitionRepository attributeDefinitionRepository,
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateAttributeDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateAttributeDefinitionCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<Guid>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        Slug slug;
        try
        {
            slug = new Slug(command.Slug!);
        }
        catch (ArgumentException)
        {
            return Result.Failure<Guid>(
                Error.Validation(CatalogErrors.InvalidSlug.Code, CatalogErrors.InvalidSlug.Description));
        }

        var name = command.Name!.Trim();

        if (await _attributeDefinitionRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateAttributeName", "An attribute definition with this name already exists."));

        if (await _attributeDefinitionRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateAttributeSlug", "An attribute definition with this slug already exists."));

        if (command.CategoryId is { } categoryId)
        {
            var category = await _categoryRepository.GetByIdAsync(categoryId, cancellationToken);

            if (category is null)
                return Result.Failure<Guid>(
                    Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

            if (category.IsDeleted)
                return Result.Failure<Guid>(
                    Error.Conflict("Catalog.DeletedCategoryCannotBeUsed", "The category is deleted and cannot be used."));
        }

        var attributeDefinition = AttributeDefinition.Create(
            Guid.NewGuid(),
            name,
            slug,
            command.DataType,
            _dateTimeProvider.UtcNow,
            command.Description,
            command.IsRequired,
            command.IsFilterable,
            command.IsSearchable,
            command.SortOrder,
            command.CategoryId);

        await _attributeDefinitionRepository.AddAsync(attributeDefinition, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(attributeDefinition.Id);
    }
}
