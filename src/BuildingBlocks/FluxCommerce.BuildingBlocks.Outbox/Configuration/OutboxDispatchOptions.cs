using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.BuildingBlocks.Outbox.Configuration;

/// <summary>
/// Phase 6.7 operational settings for the module outbox dispatchers. Each cycle recovers stale
/// claims, atomically claims a bounded batch, publishes every claimed message, and marks it
/// published (or returns it to Pending on a retryable failure). The effective retry cadence is the
/// polling interval — explicit backoff and a maximum-retry/discard policy are Phase 6.8.
/// </summary>
public sealed class OutboxDispatchOptions
{
    public const string SectionName = "OutboxDispatch";

    /// <summary>
    /// Maximum number of outbox messages dispatched per cycle. Defaults to 50.
    /// </summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Delay between two dispatch cycles. Defaults to 5 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Age after which an InProgress claim is considered stale and recovered to Pending. Defaults to
    /// 30 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
