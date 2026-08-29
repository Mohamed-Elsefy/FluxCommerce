using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Payments.Domain.Entities;

public sealed class ProcessedWebhookEvent : Entity<Guid>
{
#pragma warning disable CS8618
    private ProcessedWebhookEvent() { }
#pragma warning restore CS8618

    public ProcessedWebhookEvent(Guid id, string stripeEventId, string? eventType, DateTimeOffset processedAtUtc)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("StripeEventId is required.", nameof(stripeEventId));

        StripeEventId = stripeEventId;
        EventType = eventType ?? string.Empty;
        ProcessedAtUtc = processedAtUtc;
    }

    public string StripeEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }
}
