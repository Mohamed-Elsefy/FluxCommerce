using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;

public sealed class UpdateProductImageCommandHandler : ICommandHandler<UpdateProductImageCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public UpdateProductImageCommandHandler(
        IProductRepository productRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        UpdateProductImageCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new UpdateProductImageCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var result = product.UpdateImage(
            command.ImageId,
            command.ImageUrl!,
            command.AltText,
            command.DisplayOrder,
            command.Metadata);

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
