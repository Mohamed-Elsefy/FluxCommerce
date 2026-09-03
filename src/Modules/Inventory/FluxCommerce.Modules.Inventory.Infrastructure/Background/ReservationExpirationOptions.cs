using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Background;

/// <summary>
/// Operational settings for the reservation expiration background processor. Bounded batch sizes
/// keep each cycle short so the processor can run safely alongside the transactional workload, and
/// the fixed interval keeps the mechanism deliberately simple — no additional scheduling
/// infrastructure is introduced.
/// </summary>
public sealed class ReservationExpirationOptions
{
    public const string SectionName = "Inventory:ReservationExpiration";

    /// <summary>
    /// Delay between two processing cycles. Defaults to 30 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of expired reservations processed per cycle. Defaults to 100.
    /// </summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 100;
}
