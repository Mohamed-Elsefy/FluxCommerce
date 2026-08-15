namespace FluxCommerce.BuildingBlocks.Outbox.Messages;

public sealed record OutboxMessage
{
    public Guid Id { get; init; }

    public Guid EventId { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ProcessedAtUtc { get; init; }

    public OutboxStatus Status { get; init; }

    public int RetryCount { get; init; }

    /// <summary>
    /// Earliest UTC instant at which the message becomes eligible for re-claiming after a retryable
    /// publication failure (exponential backoff). <see langword="null"/> means the message is immediately
    /// eligible. Only meaningful while the message is <see cref="OutboxStatus.Pending"/>.
    /// </summary>
    public DateTime? RetryAfterUtc { get; init; }

    public string? Error { get; init; }
}
