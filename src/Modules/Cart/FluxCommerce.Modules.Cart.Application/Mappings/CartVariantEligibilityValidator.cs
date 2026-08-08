using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Cart.Domain.Errors;

namespace FluxCommerce.Modules.Cart.Application.Mappings;

/// <summary>
/// Shared validation step used by every Cart mutation that introduces new variants (add item,
/// Guest → Authenticated merge). Resolves all requested variants through a single batch contract
/// call and returns the first blocking Cart error or <see langword="null"/> when every variant is
/// eligible. Catalog lookup failures are never leaked across the Cart boundary — they surface as a
/// stable <c>Cart.CatalogOperationFailed</c> error. The returned results carry the minimal variant
/// snapshot data that the Cart persists with each CartItem.
/// </summary>
internal static class CartVariantEligibilityValidator
{
    public static async Task<(Error? Error, IReadOnlyCollection<CatalogVariantEligibility> Results)> ValidateAsync(
        ICatalogVariantCartLookup lookup,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var distinctIds = variantIds.Distinct().ToArray();

        if (distinctIds.Length == 0)
            return (null, Array.Empty<CatalogVariantEligibility>());

        IReadOnlyCollection<CatalogVariantEligibility> results;
        try
        {
            results = await lookup.GetCartEligibilityAsync(distinctIds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (CartErrorMapper.ToApplicationError(CartErrors.CatalogOperationFailed), Array.Empty<CatalogVariantEligibility>());
        }

        // A malformed Catalog result (missing answers, duplicate or unknown ids, nulls) is never
        // trusted: the mutation fails safely instead of proceeding with incomplete snapshot data.
        var requestedIds = distinctIds.ToHashSet();

        if (results is null
            || results.Count != distinctIds.Length
            || results.Any(result => result is null || !requestedIds.Contains(result.VariantId)))
        {
            return (CartErrorMapper.ToApplicationError(CartErrors.CatalogOperationFailed), Array.Empty<CatalogVariantEligibility>());
        }

        foreach (var result in results)
        {
            var error = CartVariantEligibilityMapper.ToError(result);

            if (error is not null)
                return (error, results);
        }

        return (null, results);
    }
}
