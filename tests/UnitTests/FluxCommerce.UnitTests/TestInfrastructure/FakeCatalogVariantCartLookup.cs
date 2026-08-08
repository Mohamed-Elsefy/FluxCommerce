using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;

namespace FluxCommerce.UnitTests.TestInfrastructure;

/// <summary>
/// Configurable <see cref="ICatalogVariantCartLookup"/> for Cart Application tests. By default every
/// variant is Available and carries a deterministic snapshot (product id = variant id, SKU derived
/// from the variant id, product name "Catalog Product"); callers can mark variants as Missing or
/// Unavailable, force the lookup to throw (Catalog operation failure), or force a malformed empty
/// result set. Every invocation is counted and the requested variant ids are recorded so tests can
/// assert the batch/N+1 behavior of the Cart Application.
/// </summary>
public sealed class FakeCatalogVariantCartLookup : ICatalogVariantCartLookup
{
    private readonly IReadOnlySet<Guid> _missing;
    private readonly IReadOnlySet<Guid> _unavailable;
    private readonly Exception? _exception;
    private readonly bool _emptyResults;
    private readonly IReadOnlyDictionary<Guid, CatalogVariantEligibility>? _snapshots;

    public FakeCatalogVariantCartLookup(
        IEnumerable<Guid>? missing = null,
        IEnumerable<Guid>? unavailable = null,
        Exception? exception = null,
        bool emptyResults = false,
        IReadOnlyDictionary<Guid, CatalogVariantEligibility>? snapshots = null)
    {
        _missing = missing?.ToHashSet() ?? new HashSet<Guid>();
        _unavailable = unavailable?.ToHashSet() ?? new HashSet<Guid>();
        _exception = exception;
        _emptyResults = emptyResults;
        _snapshots = snapshots;
    }

    public static FakeCatalogVariantCartLookup Available() => new();

    public int CallCount { get; private set; }

    public IReadOnlyCollection<Guid>? LastVariantIds { get; private set; }

    public Task<IReadOnlyCollection<CatalogVariantEligibility>> GetCartEligibilityAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastVariantIds = variantIds.ToArray();

        if (_exception is not null)
            throw _exception;

        if (_emptyResults)
            return Task.FromResult<IReadOnlyCollection<CatalogVariantEligibility>>(Array.Empty<CatalogVariantEligibility>());

        var results = variantIds
            .Select(ToEligibility)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<CatalogVariantEligibility>>(results);
    }

    private CatalogVariantEligibility ToEligibility(Guid variantId)
    {
        var status = _missing.Contains(variantId)
            ? CatalogVariantEligibilityStatus.Missing
            : _unavailable.Contains(variantId)
                ? CatalogVariantEligibilityStatus.Unavailable
                : CatalogVariantEligibilityStatus.Available;

        if (status != CatalogVariantEligibilityStatus.Available)
            return new CatalogVariantEligibility(variantId, status);

        if (_snapshots is not null && _snapshots.TryGetValue(variantId, out var snapshot))
            return snapshot;

        return new CatalogVariantEligibility(
            variantId,
            CatalogVariantEligibilityStatus.Available,
            variantId,
            $"SKU-{variantId:N}".ToUpperInvariant(),
            "Catalog Product");
    }
}
