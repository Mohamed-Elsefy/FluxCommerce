using FluxCommerce.BuildingBlocks.Infrastructure.Caching;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

public sealed class RedisCartStore : ICartStore
{
    private readonly IRedisConnectionProvider _connectionProvider;
    private readonly CartKeyGenerator _keyGenerator;
    private readonly CartStateSerializer _serializer;
    private readonly CartStateMapper _mapper;
    private readonly IOptions<CartOptions> _options;

    public RedisCartStore(
        IRedisConnectionProvider connectionProvider,
        CartKeyGenerator keyGenerator,
        CartStateSerializer serializer,
        CartStateMapper mapper,
        IOptions<CartOptions> options)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CartState?> GetAsync(CartId cartId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cartId);

        var key = _keyGenerator.ForCart(cartId);
        var value = await GetDatabase().StringGetAsync(key).ConfigureAwait(false);

        return value.HasValue ? _mapper.ToState(_serializer.Deserialize(value.ToString())) : null;
    }

    public async Task<CartState?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        var userKey = _keyGenerator.ForUser(userId);
        var cartIdValue = await database.StringGetAsync(userKey).ConfigureAwait(false);

        if (!cartIdValue.HasValue)
            return null;

        if (!Guid.TryParse(cartIdValue.ToString(), out var cartIdGuid))
            return null;

        var cartKey = _keyGenerator.ForCart(new CartId(cartIdGuid));
        var value = await database.StringGetAsync(cartKey).ConfigureAwait(false);

        return value.HasValue ? _mapper.ToState(_serializer.Deserialize(value.ToString())) : null;
    }

    public async Task<CartSaveResult> SaveAsync(CartState cart, long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var database = GetDatabase();
        var cartKey = _keyGenerator.ForCart(cart.CartId);
        var json = _serializer.Serialize(_mapper.ToDocument(cart));
        var expiry = GetExpiry(_options.Value);

        await database.StringSetAsync(cartKey, json, expiry).ConfigureAwait(false);

        if (cart.UserId is Guid userId)
        {
            var userKey = _keyGenerator.ForUser(userId);
            await database.StringSetAsync(userKey, cart.CartId.Value.ToString("D"), expiry).ConfigureAwait(false);
        }

        return new CartSaveResult(CartSaveStatus.Success, cart);
    }

    public async Task<bool> DeleteAsync(CartId cartId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cartId);

        var key = _keyGenerator.ForCart(cartId);

        return await GetDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(CartId cartId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cartId);

        var key = _keyGenerator.ForCart(cartId);

        return await GetDatabase().KeyExistsAsync(key).ConfigureAwait(false);
    }

    private IDatabase GetDatabase() => _connectionProvider.GetConnection().GetDatabase();

    internal static TimeSpan GetExpiry(CartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DefaultTtlMinutes < 1)
            throw new ArgumentException("Cart TTL must be greater than zero.", nameof(options));

        return TimeSpan.FromMinutes(options.DefaultTtlMinutes);
    }
}
