namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// How a processing failure is classified for the Phase 7.6 consumer retry policy.
/// </summary>
public enum ConsumerFailureClassification
{
    /// <summary>
    /// A transient failure that may succeed if retried. Retried in-process with backoff up to
    /// <see cref="ConsumerRetryOptions.MaxAttempts"/>.
    /// </summary>
    Retryable,

    /// <summary>
    /// A permanent failure that cannot succeed by retrying. Dead-lettered immediately (never retried).
    /// </summary>
    Permanent
}
