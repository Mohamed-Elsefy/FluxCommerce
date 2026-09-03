using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;

public sealed class ActivateAttributeDefinitionCommandHandler : ICommandHandler<ActivateAttributeDefinitionCommand, Result>
{
    private readonly IAttributeDefinitionRepository _attributeDefinitionRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILocalizer _localizer;

    public ActivateAttributeDefinitionCommandHandler(
        IAttributeDefinitionRepository attributeDefinitionRepository,
        ICatalogUnitOfWork unitOfWork,
        ILocalizer localizer)
    {
        _attributeDefinitionRepository = attributeDefinitionRepository;
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        ActivateAttributeDefinitionCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new ActivateAttributeDefinitionCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var attributeDefinition = await _attributeDefinitionRepository.GetByIdAsync(command.Id, cancellationToken);

        if (attributeDefinition is null)
            return Result.Failure(
                Error.NotFound("Catalog.AttributeDefinitionNotFound", "The attribute definition was not found."));

        var result = attributeDefinition.Activate();

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _attributeDefinitionRepository.UpdateAsync(attributeDefinition, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
