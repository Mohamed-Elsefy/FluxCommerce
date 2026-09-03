using FluxCommerce.Modules.Orders.Application.ReadModels;

namespace FluxCommerce.Modules.Orders.Application.Interfaces;

/// <summary>
/// Persistence driven port for the Order Summary projection (Phase 7.9). The projection consumer
/// adds a fully built <see cref="OrderSummaryProjection"/> to the current unit of work; the
/// <c>IIdempotentProcessor</c> boundary commits Business State + ProcessedMessage atomically, so this
/// store never saves on its own — it only stages the row for that commit.
/// </summary>
public interface IOrderSummaryProjectionStore
{
    void Add(OrderSummaryProjection projection);
}