using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;

public sealed class UpdateProductVariantCommandHandler : ICommandHandler<UpdateProductVariantCommand, Result>
{
    private readonly IVariantRepository _variantRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public UpdateProductVariantCommandHandler(
        IVariantRepository variantRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        UpdateProductVariantCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new UpdateProductVariantCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var variant = await _variantRepository.GetByIdAsync(command.Id, cancellationToken);

        if (variant is null)
            return Result.Failure(
                Error.NotFound("Catalog.VariantNotFound", "The variant was not found."));

        Sku sku;
        try
        {
            sku = new Sku(command.Sku);
        }
        catch (ArgumentException)
        {
            return Result.Failure(
                Error.Validation(CatalogErrors.InvalidSku.Code, CatalogErrors.InvalidSku.Description));
        }

        var name = command.Name!.Trim();

        if (variant.Sku != sku && await _variantRepository.ExistsBySkuAsync(sku, cancellationToken))
            return Result.Failure(
                Error.Conflict("Catalog.DuplicateVariantSku", "A variant with this SKU already exists."));

        var renameResult = variant.Rename(name);

        if (renameResult.IsFailure)
            return Result.Failure(ToApplicationError(renameResult));

        var changeSkuResult = variant.ChangeSku(sku);

        if (changeSkuResult.IsFailure)
            return Result.Failure(ToApplicationError(changeSkuResult));

        await _variantRepository.UpdateAsync(variant, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
