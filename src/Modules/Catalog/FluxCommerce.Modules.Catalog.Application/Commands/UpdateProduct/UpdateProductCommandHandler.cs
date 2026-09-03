using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new UpdateProductCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var product = await _productRepository.GetByIdAsync(command.Id, cancellationToken);

        if (product is null)
            return Result.Failure(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

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

        SeoMetadata? seo = null;

        if (command.Seo is not null)
        {
            try
            {
                seo = SeoMetadata.Create(command.Seo.Title, command.Seo.Description, command.Seo.Keywords);
            }
            catch (ArgumentException)
            {
                return Result.Failure(
                    Error.Validation(CatalogErrors.InvalidSeoMetadata.Code, CatalogErrors.InvalidSeoMetadata.Description));
            }
        }

        var name = command.Name!.Trim();

        var category = await _categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken);

        if (category is null)
            return Result.Failure(
                Error.NotFound("Catalog.CategoryNotFound", "The category was not found."));

        if (category.IsDeleted)
            return Result.Failure(
                Error.Conflict("Catalog.DeletedCategoryCannotBeUsed", "The category is deleted and cannot be used."));

        if (!string.Equals(product.Name, name, StringComparison.Ordinal) &&
            await _productRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateProductName", "A product with this name already exists."));

        if (product.Slug != slug && await _productRepository.ExistsBySlugAsync(slug, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateProductSlug", "A product with this slug already exists."));

        var renameResult = product.Rename(name);

        if (renameResult.IsFailure)
            return Result.Failure(ToApplicationError(renameResult));

        var descriptionResult = product.ChangeDescription(command.Description);

        if (descriptionResult.IsFailure)
            return Result.Failure(ToApplicationError(descriptionResult));

        var slugResult = product.ChangeSlug(slug);

        if (slugResult.IsFailure)
            return Result.Failure(ToApplicationError(slugResult));

        var seoResult = product.ChangeSeoMetadata(seo);

        if (seoResult.IsFailure)
            return Result.Failure(ToApplicationError(seoResult));

        var categoryResult = product.ChangeCategory(command.CategoryId);

        if (categoryResult.IsFailure)
            return Result.Failure(ToApplicationError(categoryResult));

        var visibilityResult = product.ChangeVisibility(command.Visibility);

        if (visibilityResult.IsFailure)
            return Result.Failure(ToApplicationError(visibilityResult));

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
