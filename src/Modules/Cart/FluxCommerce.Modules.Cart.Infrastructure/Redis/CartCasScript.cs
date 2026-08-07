namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

/// <summary>
/// Atomic compare-and-set script executed on Redis for concurrency-sensitive Cart mutations.
///
/// The script is Infrastructure-only: it enforces persistence-level concurrency semantics
/// (current value, expected version, write, version, TTL) and contains no Cart business rules.
/// The Cart version lives inside the serialized state document, so the script extracts it from
/// the stored JSON (camelCase "version" field) to compare against the expected version.
///
/// KEYS[1]   = Cart key
/// ARGV[1]   = new serialized Cart state document
/// ARGV[2]   = expected version (long)
/// ARGV[3]   = TTL in seconds (long)
///
/// Result codes:
/// 0 = success (state written and TTL applied/refreshed)
/// 1 = version conflict (stale expected version; nothing written, TTL untouched)
/// 2 = cart missing (expected version > 0 but no Cart key exists; nothing written, TTL untouched)
/// 3 = corrupt stored state (schema version unsupported or version field unreadable)
/// </summary>
internal static class CartCasScript
{
    internal const int Success = 0;

    internal const int VersionConflict = 1;

    internal const int CartMissing = 2;

    internal const int CorruptState = 3;

    internal static string Source { get; } = $"""
        local current = redis.call('GET', KEYS[1])
        local expected = tonumber(ARGV[2])
        local ttl = tonumber(ARGV[3])

        if not current then
            if expected == 0 then
                redis.call('SET', KEYS[1], ARGV[1])
                if ttl > 0 then redis.call('EXPIRE', KEYS[1], ttl) end
                return 0
            end
            return 2
        end

        local schemaStart, _, schema = string.find(current, '"schemaVersion":(%d+)')
        if not schemaStart or tonumber(schema) ~= {CartStateSerializer.SchemaVersion} then
            return 3
        end

        local versionStart, _, version = string.find(current, '"version":(%d+)')
        if not versionStart then
            return 3
        end

        if tonumber(version) ~= expected then
            return 1
        end

        redis.call('SET', KEYS[1], ARGV[1])
        if ttl > 0 then redis.call('EXPIRE', KEYS[1], ttl) end
        return 0
        """;
}
