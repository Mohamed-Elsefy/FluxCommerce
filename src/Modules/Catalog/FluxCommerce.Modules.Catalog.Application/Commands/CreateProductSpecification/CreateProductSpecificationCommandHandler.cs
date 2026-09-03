using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;

public sealed class CreateProductSpecificationCommandHandler : ICommandHandler<CreateProductSpecificationCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public CreateProductSpecificationCommandHandler(
        IProductRepository productRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        CreateProductSpecificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new CreateProductSpecificationCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var result = product.AddSpecification(command.Name!, command.Value!, _dateTimeProvider.UtcNow);

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
