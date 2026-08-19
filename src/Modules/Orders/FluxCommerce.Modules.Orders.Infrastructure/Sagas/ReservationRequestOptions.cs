using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Orders.Infrastructure.Sagas;

/// <summary>
/// Phase 8.3 operational settings for the reservation requests issued by the Order/Inventory Saga
/// orchestrator. <see cref="ReservationLifetime"/> is the deterministic deadline of every reservation
/// request produced for an order: it is applied at place-time (<c>occurredAtUtc + lifetime</c>), so a
/// redelivered <c>orders.order_created</c> never shifts the deadline. Registered with defaults only,
/// mirroring the module outbox dispatch options.
/// </summary>
public sealed class ReservationRequestOptions
{
    public const string SectionName = "ReservationRequest";

    /// <summary>
    /// Lifetime of an inventory reservation requested by the saga, measured from the moment the order
    /// was placed. Defaults to 30 minutes.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan ReservationLifetime { get; set; } = TimeSpan.FromMinutes(30);
}