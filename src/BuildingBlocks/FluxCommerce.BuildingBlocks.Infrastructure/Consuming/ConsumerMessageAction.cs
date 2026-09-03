namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// The transport action a processed delivery must take, returned by the message processor and applied
/// by the consumer host on the delivery's channel:
/// <list type="bullet">
/// <item><see cref="Ack"/> — the message was handled (newly processed, a duplicate, or an unhandled
/// event type) and is acknowledged (BasicAck). A message is only acked after the business operation and
/// idempotency transaction have committed.</item>
/// <item><see cref="Requeue"/> — an unexpected host-level failure left the delivery unfinished and the
/// message is returned to the queue for redelivery (BasicNack, requeue=true). At-Least-Once semantics:
/// an uncommitted failure is never acked. Retryable processing failures are retried in-process by the
/// processor (Phase 7.6) and do not return here.</item>
/// <item><see cref="Reject"/> — the message is permanently unprocessable (malformed payload, invalid
/// metadata) and is rejected without requeue (BasicNack, requeue=false). With the queue's
/// dead-letter-exchange the message is routed to the dead-letter queue.</item>
/// <item><see cref="DeadLetter"/> — processing was attempted and terminated (retries exhausted, or a
/// permanent non-retryable failure) and the message is rejected without requeue (BasicNack,
/// requeue=false), routing it to the dead-letter queue for diagnosis.</item>
/// </list>
/// </summary>
public enum ConsumerMessageAction
{
    Ack,
    Requeue,
    Reject,
    DeadLetter
}
