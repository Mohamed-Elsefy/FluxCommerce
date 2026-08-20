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

    /// <summary>
    /// The W3C <c>traceparent</c> of the producing activity captured when the message was staged (Phase
    /// 8.9). The outbox dispatcher restores it as the parent of its publishing activity, so the trace
    /// survives the asynchronous outbox hop. <see langword="null"/> when the message was staged outside an
    /// active W3C trace.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// The W3C <c>tracestate</c> of the producing activity captured when the message was staged (Phase
    /// 8.9), propagated alongside <see cref="TraceParent"/>.
    /// </summary>
    public string? TraceState { get; init; }
}
