using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Controllable Cart checkout snapshot used by the Checkout API integration tests. The real Cart
/// store (Redis) is deliberately left out of the test scope; the application's Cart-to-Orders
/// contract is exercised against this deterministic provider.
/// </summary>
public sealed class FakeCartCheckoutSnapshotProvider : ICartCheckoutSnapshotProvider
{
    private readonly Dictionary<Guid, CartCheckoutSnapshot> _carts = new();
    private readonly object _lock = new();

    public int Calls { get; private set; }

    public void Seed(Guid cartId, Guid? customerId, params CartCheckoutItemSnapshot[] items)
    {
        lock (_lock)
        {
            _carts[cartId] = new CartCheckoutSnapshot(cartId, customerId, items.ToList());
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _carts.Clear();
        }
    }

    public Task<CartCheckoutSnapshot?> GetCheckoutSnapshotAsync(
        Guid cartId,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            _carts.TryGetValue(cartId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }
}

public sealed class FakeCartCheckoutConsumption : ICartCheckoutConsumption
{
    private readonly object _lock = new();
    private CartCheckoutConsumptionStatus _status = CartCheckoutConsumptionStatus.Consumed;
    private bool _throw;

    public int Calls { get; private set; }

    public Guid? LastCartId { get; private set; }

    public Guid? LastCustomerId { get; private set; }

    public void Reset()
    {
        lock (_lock)
        {
            Calls = 0;
            LastCartId = null;
            LastCustomerId = null;
            _status = CartCheckoutConsumptionStatus.Consumed;
            _throw = false;
        }
    }

    public void WithStatus(CartCheckoutConsumptionStatus status)
    {
        lock (_lock)
        {
            _status = status;
            _throw = false;
        }
    }

    public void WithThrow()
    {
        lock (_lock)
        {
            _throw = true;
        }
    }

    public Task<CartCheckoutConsumptionResult> ConsumeAsync(
        Guid cartId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            LastCartId = cartId;
            LastCustomerId = customerId;

            if (_throw)
                throw new InvalidOperationException("Simulated Cart consumption failure.");

            return Task.FromResult(new CartCheckoutConsumptionResult(_status));
        }
    }
}

/// <summary>
/// Controllable Catalog variant revalidation used by the Checkout API integration tests. Variants
/// seeded as available provide the authoritative current price; everything else is unavailable.
/// </summary>
public sealed class FakeCatalogCheckoutLookup : ICatalogVariantCheckoutLookup
{
    private readonly Dictionary<Guid, CatalogVariantCheckoutInfo> _variants = new();
    private readonly object _lock = new();

    public void Seed(CatalogVariantCheckoutInfo info)
    {
        lock (_lock)
        {
            _variants[info.VariantId] = info;
        }
    }

    public Task<IReadOnlyCollection<CatalogVariantCheckoutInfo>> GetCheckoutInfoAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<CatalogVariantCheckoutInfo>>(
            variantIds
                .Select(id => Resolve(id))
                .ToList());

    private CatalogVariantCheckoutInfo Resolve(Guid variantId)
    {
        lock (_lock)
        {
            return _variants.TryGetValue(variantId, out var info)
                ? info
                : new CatalogVariantCheckoutInfo(variantId, CatalogVariantEligibilityStatus.Unavailable);
        }
    }
}