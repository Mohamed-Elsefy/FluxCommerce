namespace FluxCommerce.Modules.Orders.Application.ReadModels;

/// <summary>
/// Lightweight daily-series point for an Order: creation timestamp only. Order counts per day
/// are bucketed in memory by the consumer; revenue comes from the Payments side, so no monetary
/// projection (which would require aggregating line items in the database) is included.
/// </summary>
public sealed record OrderDailyPointReadModel(
    DateTimeOffset CreatedAtUtc);
