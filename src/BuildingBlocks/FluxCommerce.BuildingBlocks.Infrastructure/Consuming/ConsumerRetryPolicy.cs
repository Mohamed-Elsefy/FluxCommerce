namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Pure Phase 7.6 retry-policy math for the consumer: when attempts are exhausted and the backoff
/// delay to wait before the next attempt. Deliberately separate from the Phase 6 outbox publication
/// retry policy — this is the consumer processing retry only.
/// </summary>
public static class ConsumerRetryPolicy
{
    /// <summary>
    /// Returns <c>true</c> once <paramref name="attempt"/> (1-based) reaches
    /// <see cref="ConsumerRetryOptions.MaxAttempts"/> — no further retries are allowed and the message
    /// must be dead-lettered.
    /// </summary>
    public static bool IsExhausted(ConsumerRetryOptions options, int attempt) =>
        attempt >= options.MaxAttempts;

    /// <summary>
    /// Computes the backoff delay to wait BEFORE the next attempt, given the attempt that just failed
    /// (<paramref name="attempt"/> is 1-based; a failure on attempt 1 waits for the 2nd attempt).
    /// The delay follows <see cref="ConsumerRetryOptions.BackoffType"/> and never exceeds
    /// <see cref="ConsumerRetryOptions.MaxDelay"/>.
    /// </summary>
    public static TimeSpan GetDelay(ConsumerRetryOptions options, int attempt)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), "The attempt must be at least 1.");

        var delayMilliseconds = options.BackoffType switch
        {
            ConsumerBackoffType.Fixed => options.InitialDelay.TotalMilliseconds,
            ConsumerBackoffType.Linear => options.InitialDelay.TotalMilliseconds * attempt,
            ConsumerBackoffType.Exponential =>
                options.InitialDelay.TotalMilliseconds * Math.Pow(options.DelayMultiplier, attempt - 1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options.BackoffType),
                options.BackoffType,
                "Unknown consumer backoff type.")
        };

        delayMilliseconds = Math.Min(delayMilliseconds, options.MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(delayMilliseconds > 0 ? delayMilliseconds : 0);
    }
}
