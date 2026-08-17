namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Configuration for the Phase 7.5 RabbitMQ consumer host. The consumer consumes from the SAME
/// Phase 6 topology (the durable topic <see cref="ExchangeName"/>) rather than creating a parallel
/// messaging architecture: it declares the exchange and a single <see cref="QueueName"/> and binds
/// the queue to each registered event type's routing key. <see cref="Concurrency"/> bounded consumers
/// run against the same queue (the broker round-robins deliveries), each with its own channel and
/// <see cref="PrefetchCount"/> prefetch, giving bounded in-flight concurrency. Delivery is manual:
/// every message is acknowledged only after processing completes (never auto-acked).
/// </summary>
public sealed class RabbitMQConsumerOptions
{
    public const string SectionName = "RabbitMQConsumer";

    /// <summary>The queue this consumer consumes from. Must be provided.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>The Phase 6 durable topic exchange the queue binds to. Must be provided.</summary>
    public string ExchangeName { get; set; } = string.Empty;

    /// <summary>Per-channel unacknowledged-delivery limit. Defaults to 1 (one message in flight per consumer).</summary>
    public ushort PrefetchCount { get; set; } = 1;

    /// <summary>Number of consumers/channels running against the queue. Defaults to 1.</summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>Whether the queue survives a broker restart. Defaults to true.</summary>
    public bool Durable { get; set; } = true;

    /// <summary>Whether the queue is deleted when the consumer disconnects. Defaults to false.</summary>
    public bool AutoDelete { get; set; }

    /// <summary>
    /// The Phase 7.6 consumer retry policy (in-process backoff + bounded attempts + terminal
    /// dead-lettering). Independent of the Phase 6 outbox publication retry.
    /// </summary>
    public ConsumerRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// The dead-letter exchange terminally failed deliveries are routed to (malformed messages,
    /// permanent failures, exhausted retries). Defaults to <c>{QueueName}.dlx</c> when empty.
    /// </summary>
    public string DeadLetterExchangeName { get; set; } = string.Empty;

    /// <summary>
    /// The dead-letter queue terminal failures are routed to for diagnosis. Defaults to
    /// <c>{QueueName}.dlq</c> when empty.
    /// </summary>
    public string DeadLetterQueueName { get; set; } = string.Empty;

    /// <summary>
    /// The dead-letter exchange in effect: <see cref="DeadLetterExchangeName"/> when provided, else the
    /// derived <c>{QueueName}.dlx</c>. Used by both the consumer host (topology declaration) and the
    /// dead-letter publisher (terminal-failure enrichment).
    /// </summary>
    public string ResolvedDeadLetterExchangeName =>
        string.IsNullOrWhiteSpace(DeadLetterExchangeName) ? $"{QueueName}.dlx" : DeadLetterExchangeName;

    /// <summary>
    /// The dead-letter queue in effect: <see cref="DeadLetterQueueName"/> when provided, else the derived
    /// <c>{QueueName}.dlq</c>.
    /// </summary>
    public string ResolvedDeadLetterQueueName =>
        string.IsNullOrWhiteSpace(DeadLetterQueueName) ? $"{QueueName}.dlq" : DeadLetterQueueName;

    /// <summary>
    /// Validates the consumer configuration. Throws <see cref="InvalidOperationException"/> when a
    /// required value is missing or a value would produce unsafe behavior. Called by the consumer host
    /// on startup so misconfiguration fails fast. In particular the dead-letter topology must not alias
    /// the main topology: a dead-letter exchange equal to the main exchange or a dead-letter queue equal
    /// to the main queue would route terminal failures straight back into the processing loop (endless
    /// retry), which the retry policy must never allow.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueueName))
            throw new InvalidOperationException("The RabbitMQ consumer requires a queue name.");

        if (string.IsNullOrWhiteSpace(ExchangeName))
            throw new InvalidOperationException("The RabbitMQ consumer requires an exchange name.");

        if (PrefetchCount < 1)
            throw new InvalidOperationException("The RabbitMQ consumer prefetch count must be at least 1.");

        if (Concurrency < 1)
            throw new InvalidOperationException("The RabbitMQ consumer concurrency must be at least 1.");

        Retry.Validate();

        if (ResolvedDeadLetterExchangeName == ExchangeName)
            throw new InvalidOperationException(
                "The dead-letter exchange must not be the consumer's main exchange; terminal failures routed back to the main exchange would be processed again (endless retry).");

        if (ResolvedDeadLetterQueueName == QueueName)
            throw new InvalidOperationException(
                "The dead-letter queue must not be the consumer's main queue; terminal failures routed back to the main queue would be processed again (endless retry).");
    }
}