using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;

public sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Result<Guid>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateCategoryCommandValidator(_localizer).Validate(command);

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

        if (await _categoryRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateCategoryName", "A category with this name already exists."));

        if (await _categoryRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateCategorySlug", "A category with this slug already exists."));

        if (command.ParentId is { } parentId)
        {
            var parent = await _categoryRepository.GetByIdAsync(parentId, cancellationToken);

            if (parent is null)
                return Result.Failure<Guid>(
                    Error.NotFound("Catalog.ParentCategoryNotFound", "The parent category was not found."));

            if (parent.IsDeleted)
                return Result.Failure<Guid>(
                    Error.Conflict("Catalog.ParentCategoryDeleted", "The parent category is deleted and cannot be used."));
        }

        var category = Category.Create(
            Guid.NewGuid(),
            name,
            slug,
            _dateTimeProvider.UtcNow,
            command.Description,
            command.ParentId);

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(category.Id);
    }
}
