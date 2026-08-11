using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;

public sealed class CreateProductVariantCommandHandler : ICommandHandler<CreateProductVariantCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly IVariantRepository _variantRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateProductVariantCommandHandler(
        IProductRepository productRepository,
        IVariantRepository variantRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreateProductVariantCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateProductVariantCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<Guid>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        Sku sku;
        try
        {
            sku = new Sku(command.Sku);
        }
        catch (ArgumentException)
        {
            return Result.Failure<Guid>(
                Error.Validation(CatalogErrors.InvalidSku.Code, CatalogErrors.InvalidSku.Description));
        }

        Money unitPrice;
        try
        {
            unitPrice = new Money(command.UnitPriceAmount, command.Currency);
        }
        catch (ArgumentException)
        {
            return Result.Failure<Guid>(
                Error.Validation(CatalogErrors.InvalidUnitPrice.Code, CatalogErrors.InvalidUnitPrice.Description));
        }

        var name = command.Name!.Trim();

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<Guid>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        if (await _variantRepository.ExistsBySkuAsync(sku, cancellationToken))
            return Result.Failure<Guid>(
                Error.Conflict("Catalog.DuplicateVariantSku", "A variant with this SKU already exists."));

        var variant = Variant.Create(
            Guid.NewGuid(),
            command.ProductId,
            sku,
            name,
            unitPrice,
            _dateTimeProvider.UtcNow);

        await _variantRepository.AddAsync(variant, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(variant.Id);
    }
}
