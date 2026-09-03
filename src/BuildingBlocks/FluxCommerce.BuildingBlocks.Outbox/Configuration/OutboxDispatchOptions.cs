using System.ComponentModel.DataAnnotations;
using FluxCommerce.BuildingBlocks.Outbox.Messages;

namespace FluxCommerce.BuildingBlocks.Outbox.Configuration;

/// <summary>
/// Phase 6.7/6.8 operational settings for the module outbox dispatchers. Each cycle recovers stale
/// claims, atomically claims a bounded batch, publishes every claimed message, and marks it
/// published (or returns it to Pending on a retryable failure). Retryable failures are scheduled
/// for re-claiming after an exponential backoff (<see cref="BaseRetryDelay"/>,
/// <see cref="RetryDelayMultiplier"/>, <see cref="MaxRetryDelay"/>); a message that still fails once
/// it has reached <see cref="MaxRetryCount"/> retries is moved to the terminal Failed state so
/// poison messages cannot be retried forever.
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

    /// <summary>
    /// Maximum number of retryable failures a message may survive before it is moved to the terminal
    /// <see cref="OutboxStatus.Failed"/> state. A message is first attempted once; after that it may be
    /// retried up to this many times. Defaults to 5.
    /// </summary>
    [Range(0, 1000)]
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Delay scheduled before the first re-claim attempt after a retryable failure. The delay for
    /// subsequent attempts grows by <see cref="RetryDelayMultiplier"/> each time. Defaults to 1 second.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Factor applied to the backoff delay after every retryable failure (1.0 disables exponential
    /// growth). Defaults to 2.0.
    /// </summary>
    [Range(1.0, 10.0)]
    public double RetryDelayMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Upper bound for the exponential backoff delay. Defaults to 60 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}
