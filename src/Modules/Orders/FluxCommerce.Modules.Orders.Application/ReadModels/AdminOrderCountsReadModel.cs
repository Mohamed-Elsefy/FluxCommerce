using FluxCommerce.Modules.Orders.Domain.Enums;

namespace FluxCommerce.Modules.Orders.Application.ReadModels;

/// <summary>
/// Aggregate Order counts grouped by status for the administrative overview.
/// </summary>
public sealed record AdminOrderCountsReadModel(
    int Total,
    int Pending,
    int Confirmed,
    int Cancelled)
{
    /// <summary>
    /// Builds the overview from raw database status groups. The store runs the
    /// grouping query; the status breakdown lives here in Application so the
    /// store stays a thin persistence adapter.
    /// </summary>
    public static AdminOrderCountsReadModel FromStatusGroups(
        IEnumerable<(OrderStatus Status, int Count)> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var pending = 0;
        var confirmed = 0;
        var cancelled = 0;

        foreach (var group in groups)
        {
            switch (group.Status)
            {
                case OrderStatus.Pending:
                    pending = group.Count;
                    break;
                case OrderStatus.Confirmed:
                    confirmed = group.Count;
                    break;
                case OrderStatus.Cancelled:
                    cancelled = group.Count;
                    break;
            }
        }

        return new AdminOrderCountsReadModel(
            pending + confirmed + cancelled,
            pending,
            confirmed,
            cancelled);
    }
}
