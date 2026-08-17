namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// The delay-growth strategy applied between consumer processing attempts.
/// </summary>
public enum ConsumerBackoffType
{
    /// <summary>Every retry waits the same fixed <see cref="ConsumerRetryOptions.InitialDelay"/>.</summary>
    Fixed,

    /// <summary>Retry <c>N</c> waits <c>InitialDelay * N</c> (linear growth).</summary>
    Linear,

    /// <summary>
    /// Retry <c>N</c> waits <c>InitialDelay * DelayMultiplier^(N-1)</c> (exponential growth), capped by
    /// <see cref="ConsumerRetryOptions.MaxDelay"/>.
    /// </summary>
    Exponential
}

/// <summary>
/// Phase 7.6 consumer retry policy. This is the CONSUMER-side processing retry and is deliberately
/// separate from the Phase 6 outbox PUBLICATION retry (<c>OutboxDispatchOptions</c>) — the two solve
/// different problems (publishing from the outbox vs. consuming from the broker) and are never merged.
///
/// <para>
/// The policy is bounded and deterministic: a retryable processing failure is retried in-process with
/// a configurable backoff, up to <see cref="MaxAttempts"/> total processing attempts. Once attempts are
/// exhausted — or the failure is classified <see cref="ConsumerFailureClassification.Permanent"/> —
/// the message reaches a terminal state and is dead-lettered (it is never retried indefinitely).
/// </para>
/// </summary>
public sealed class ConsumerRetryOptions
{
    /// <summary>
    /// Total processing attempts allowed for one delivery, including the first. Defaults to 3 (one
    /// initial attempt + two retries). Set to 1 to disable retries (any failure is terminal).
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The delay-growth strategy between attempts. Defaults to <see cref="ConsumerBackoffType.Exponential"/>.</summary>
    public ConsumerBackoffType BackoffType { get; set; } = ConsumerBackoffType.Exponential;

    /// <summary>The base delay between attempts. Defaults to 1 second.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The growth multiplier for <see cref="ConsumerBackoffType.Exponential"/>. Defaults to 2.0.
    /// </summary>
    public double DelayMultiplier { get; set; } = 2.0;

    /// <summary>The upper bound for any single backoff delay. Defaults to 30 seconds.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exception types treated as permanent (non-retryable). An exception of one of these types — or
    /// wrapped inside one (inner exceptions are inspected) — is classified
    /// <see cref="ConsumerFailureClassification.Permanent"/> and dead-lettered immediately without
    /// consuming retry attempts. All other failures are retryable by default. Empty by default.
    /// </summary>
    public List<Type> NonRetryableExceptionTypes { get; set; } = [];

    /// <summary>
    /// Validates the retry policy. Throws <see cref="InvalidOperationException"/> when the
    /// configuration would allow an illegal retry/backoff shape (negative delays, a multiplier below
    /// 1.0, a MaxDelay below InitialDelay, an attempt budget below 1, or an undefined backoff type).
    /// Called by the consumer host on startup so misconfiguration fails fast instead of producing
    /// degenerate retry behavior.
    /// </summary>
    public void Validate()
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.MaxAttempts must be at least 1 (configured {MaxAttempts}); set 1 to disable retries.");

        if (!Enum.IsDefined(BackoffType))
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.BackoffType must be a defined {nameof(ConsumerBackoffType)} value (configured {BackoffType}).");

        if (InitialDelay < TimeSpan.Zero)
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.InitialDelay must be non-negative (configured {InitialDelay}).");

        if (DelayMultiplier < 1.0)
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.DelayMultiplier must be at least 1.0 (configured {DelayMultiplier}); values below 1.0 would shrink the backoff.");

        if (MaxDelay < TimeSpan.Zero)
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.MaxDelay must be non-negative (configured {MaxDelay}).");

        if (MaxDelay < InitialDelay)
            throw new InvalidOperationException(
                $"ConsumerRetryOptions.MaxDelay ({MaxDelay}) must be at least InitialDelay ({InitialDelay}).");

        if (NonRetryableExceptionTypes is null)
            throw new InvalidOperationException("ConsumerRetryOptions.NonRetryableExceptionTypes must not be null.");
    }
}
