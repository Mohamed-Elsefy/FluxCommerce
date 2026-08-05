using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.AssignProductAttributeValue;

public sealed class AssignProductAttributeValueCommandHandler : ICommandHandler<AssignProductAttributeValueCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public AssignProductAttributeValueCommandHandler(
        IProductRepository productRepository,
        IAttributeDefinitionRepository attributeDefinitionRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _productRepository = productRepository;
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        AssignProductAttributeValueCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new AssignProductAttributeValueCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure(
                Error.NotFound("Catalog.ProductNotFound", "The product was not found."));

        var attributeDefinition = await _attributeDefinitionRepository.GetByIdAsync(command.AttributeDefinitionId, cancellationToken);

        if (attributeDefinition is null)
            return Result.Failure(
                Error.NotFound("Catalog.AttributeDefinitionNotFound", "The attribute definition was not found."));

        var result = product.AssignAttributeValue(attributeDefinition, command.Value!, _dateTimeProvider.UtcNow);

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
