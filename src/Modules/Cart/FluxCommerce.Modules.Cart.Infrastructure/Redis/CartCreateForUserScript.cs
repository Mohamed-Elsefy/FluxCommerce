namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

/// <summary>
/// Atomic create script for an authenticated Cart executed on Redis. It creates the Cart key
/// together with the user index key only when the user has no active Cart yet, so one-active-Cart
/// is enforced atomically even under concurrent creation requests.
///
/// The script is Infrastructure-only: it enforces persistence-level semantics (key existence, write,
/// TTL) and contains no Cart business rules.
///
/// KEYS[1]   = Cart key
/// KEYS[2]   = user index key (cart:user:{userId})
/// ARGV[1]   = new serialized Cart state document
/// ARGV[2]   = TTL in seconds (long)
/// ARGV[3]   = Cart id string stored as the user index value
///
/// Result codes:
/// 0 = created (Cart key and user index key set with TTL)
/// 1 = Cart key already exists (unexpected for a fresh identifier; nothing written, TTL untouched)
/// 2 = user already has an active Cart (duplicate; nothing written, TTL untouched)
/// </summary>
internal static class CartCreateForUserScript
{
    internal const int Success = 0;

    internal const int CartAlreadyExists = 1;

    internal const int DuplicateActiveCart = 2;

    internal static string Source { get; } = $"""
        if redis.call('EXISTS', KEYS[2]) == 1 then
            return 2
        end

        if redis.call('EXISTS', KEYS[1]) == 1 then
            return 1
        end

        local ttl = tonumber(ARGV[2])

        redis.call('SET', KEYS[1], ARGV[1])
        if ttl > 0 then redis.call('EXPIRE', KEYS[1], ttl) end

        redis.call('SET', KEYS[2], ARGV[3])
        if ttl > 0 then redis.call('EXPIRE', KEYS[2], ttl) end

        return 0
        """;
}
