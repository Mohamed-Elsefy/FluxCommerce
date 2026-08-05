using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;

public sealed class DeleteProductVariantCommandHandler : ICommandHandler<DeleteProductVariantCommand, Result>
{
    private readonly IVariantRepository _variantRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILocalizer _localizer;

    public DeleteProductVariantCommandHandler(
        IVariantRepository variantRepository,
        ICatalogUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILocalizer localizer)
    {
        _variantRepository = variantRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _localizer = localizer;
    }

    public async Task<Result> HandleAsync(
        DeleteProductVariantCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new DeleteProductVariantCommandValidator(_localizer).Validate(command);

        if (!validation.IsValid)
            return Result.Failure(
                Error.Validation("Catalog.ValidationFailed", "One or more fields are invalid."));

        var variant = await _variantRepository.GetByIdAsync(command.Id, cancellationToken);

        if (variant is null)
            return Result.Failure(
                Error.NotFound("Catalog.VariantNotFound", "The variant was not found."));

        var result = variant.SoftDelete(_dateTimeProvider.UtcNow);

        if (result.IsFailure)
            return Result.Failure(ToApplicationError(result));

        await _variantRepository.UpdateAsync(variant, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Error ToApplicationError(DomainResult result) =>
        Error.Conflict(result.Error.Code, result.Error.Description);
}
