namespace FluxCommerce.Modules.Orders.Application.ReadModels;

/// <summary>
/// Aggregate Order counts grouped by status for the administrative overview.
/// </summary>
public sealed record AdminOrderCountsReadModel(
    int Total,
    int Pending,
    int Confirmed,
    int Cancelled);
