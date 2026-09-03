namespace FluxCommerce.Modules.Cart.Infrastructure.Redis;

/// <summary>
/// Atomic merge-and-consume script executed on Redis for the Guest → Authenticated Cart merge
/// (Phase 3.6.2/3.6.3). It writes the merged target Cart under the same optimistic-concurrency CAS
/// as <see cref="CartCasScript"/> and deletes the Guest source Cart key in the same atomic operation.
///
/// The script additionally verifies that the source Cart is still present and unchanged before
/// committing. This closes the concurrent double-apply race: a merge attempt that read the source
/// before a concurrent attempt consumed it can no longer commit against a target that already
/// contains the same Guest quantities, so the Guest quantities can never be applied twice (A×7).
/// The source is consumed only when the target write succeeds, so a failed merge never consumes
/// the source and a retried merge can never re-apply the Guest quantities.
///
/// The script is Infrastructure-only: it enforces persistence-level concurrency semantics (current
/// value, expected version, write, version, TTL, key deletion) and contains no Cart business rules.
///
/// KEYS[1]   = target Cart key
/// KEYS[2]   = source (Guest) Cart key
/// ARGV[1]   = new serialized target Cart state document
/// ARGV[2]   = expected target version (long)
/// ARGV[3]   = TTL in seconds (long)
/// ARGV[4]   = expected source (Guest) Cart version (long)
///
/// Result codes:
/// 0 = success (target written, TTL applied/refreshed, source key deleted)
/// 1 = target version conflict (stale expected target version; nothing written, nothing deleted)
/// 2 = target cart missing (no target Cart key exists; nothing written, nothing deleted)
/// 3 = corrupt stored state (schema version unsupported or version field unreadable)
/// 4 = source already consumed (source Cart key missing; nothing written, nothing deleted)
/// 5 = source modified concurrently (source version mismatch; nothing written, nothing deleted)
/// </summary>
internal static class CartMergeConsumeScript
{
    internal const int Success = 0;

    internal const int TargetVersionConflict = 1;

    internal const int TargetCartMissing = 2;

    internal const int CorruptState = 3;

    internal const int SourceConsumed = 4;

    internal const int SourceModified = 5;

    internal static string Source { get; } = $"""
        local target = redis.call('GET', KEYS[1])
        local expectedTargetVersion = tonumber(ARGV[2])
        local ttl = tonumber(ARGV[3])
        local expectedSourceVersion = tonumber(ARGV[4])

        if not target then
            return 2
        end

        local targetSchemaStart, _, targetSchema = string.find(target, '"schemaVersion":(%d+)')
        if not targetSchemaStart or tonumber(targetSchema) ~= {CartStateSerializer.SchemaVersion} then
            return 3
        end

        local targetVersionStart, _, targetVersion = string.find(target, '"version":(%d+)')
        if not targetVersionStart then
            return 3
        end

        if tonumber(targetVersion) ~= expectedTargetVersion then
            return 1
        end

        local source = redis.call('GET', KEYS[2])
        if not source then
            return 4
        end

        local sourceSchemaStart, _, sourceSchema = string.find(source, '"schemaVersion":(%d+)')
        if not sourceSchemaStart or tonumber(sourceSchema) ~= {CartStateSerializer.SchemaVersion} then
            return 3
        end

        local sourceVersionStart, _, sourceVersion = string.find(source, '"version":(%d+)')
        if not sourceVersionStart then
            return 3
        end

        if tonumber(sourceVersion) ~= expectedSourceVersion then
            return 5
        end

        redis.call('SET', KEYS[1], ARGV[1])
        if ttl > 0 then redis.call('EXPIRE', KEYS[1], ttl) end
        redis.call('DEL', KEYS[2])
        return 0
        """;
}
