using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateProductCommandValidator(_localizer).Validate(command);

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

        SeoMetadata? seo = null;

        if (command.Seo is not null)
        {
            try
            {
                seo = SeoMetadata.Create(command.Seo.Title, command.Seo.Description, command.Seo.Keywords);
            }
            catch (ArgumentException)
            {
                return Result.Failure<Guid>(
                    Error.Validation(CatalogErrors.InvalidSeoMetadata.Code, CatalogErrors.InvalidSeoMetadata.Description));
            }
        }

        var name = command.Name!.Trim();

        var category = await _categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken);

        if (category is null)
            return Result.Failure<Guid>(
                Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

        if (category.IsDeleted)
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DeletedCategoryCannotBeUsed", "The category is deleted and cannot be used."));

        if (await _productRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateProductName", "A product with this name already exists."));

        if (await _productRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateProductSlug", "A product with this slug already exists."));

        var product = Product.Create(
            Guid.NewGuid(),
            name,
            slug,
            command.CategoryId,
            _dateTimeProvider.UtcNow,
            command.Description,
            seo,
            command.Visibility);

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(product.Id);
    }
}
