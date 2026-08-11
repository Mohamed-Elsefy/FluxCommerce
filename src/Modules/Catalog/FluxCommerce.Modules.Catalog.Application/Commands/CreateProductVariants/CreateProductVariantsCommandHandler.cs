using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Errors;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;

public sealed class CreateProductVariantsCommandHandler : ICommandHandler<CreateProductVariantsCommand, Result<IReadOnlyCollection<Guid>>>
{
    private readonly IProductRepository _productRepository;
    private readonly IVariantRepository _variantRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateProductVariantsCommandHandler(
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

    public async Task<Result<IReadOnlyCollection<Guid>>> HandleAsync(
        CreateProductVariantsCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateProductVariantsCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure<IReadOnlyCollection<Guid>>(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var skus = new Sku[command.Variants.Count];
        var unitPrices = new Money[command.Variants.Count];

        for (var i = 0; i < command.Variants.Count; i++)
        {
            try
            {
                skus[i] = new Sku(command.Variants[i].Sku);
            }
            catch (ArgumentException)
            {
                return Result.Failure<IReadOnlyCollection<Guid>>(
                    Error.Validation(CatalogErrors.InvalidSku.Code, CatalogErrors.InvalidSku.Description));
            }

            try
            {
                unitPrices[i] = new Money(command.Variants[i].UnitPriceAmount, command.Variants[i].Currency);
            }
            catch (ArgumentException)
            {
                return Result.Failure<IReadOnlyCollection<Guid>>(
                    Error.Validation(CatalogErrors.InvalidUnitPrice.Code, CatalogErrors.InvalidUnitPrice.Description));
            }
        }

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<IReadOnlyCollection<Guid>>(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < skus.Length; i++)
        {
            var sku = skus[i];

            if (!seen.Add(sku.Value))
                return Result.Failure<IReadOnlyCollection<Guid>>(
                    Error.Conflict("Catalog.DuplicateVariantSku", "A variant with this SKU already exists."));

            if (await _variantRepository.ExistsBySkuAsync(sku, cancellationToken))
                return Result.Failure<IReadOnlyCollection<Guid>>(
                    Error.Conflict("Catalog.DuplicateVariantSku", "A variant with this SKU already exists."));
        }

        var now = _dateTimeProvider.UtcNow;
        var created = new List<Guid>(command.Variants.Count);

        for (var i = 0; i < command.Variants.Count; i++)
        {
            var variant = Variant.Create(
                Guid.NewGuid(),
                command.ProductId,
                skus[i],
                command.Variants[i].Name!.Trim(),
                unitPrices[i],
                now);

            created.Add(variant.Id);
            await _variantRepository.AddAsync(variant, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success<IReadOnlyCollection<Guid>>(created);
    }
}
