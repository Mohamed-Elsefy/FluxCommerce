using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Inventory.Domain.Errors;

namespace FluxCommerce.Modules.Inventory.Application.Errors;

/// <summary>
/// Maps a Domain-level reservation error to the application error surfaced by the lifecycle use
/// cases. Domain codes are preserved so later localization and ProblemDetails mapping stay stable.
/// </summary>
public static class InventoryReservationErrorMapper
{
    public static Error Map(DomainError error)
    {
        if (error == InventoryErrors.InvalidQuantity)
            return InventoryApplicationErrors.InvalidQuantity;

        if (error == InventoryErrors.InvalidExpiration)
            return InventoryReservationErrors.InvalidExpiration;

        if (error == InventoryErrors.InsufficientStock)
            return InventoryReservationErrors.InsufficientStock;

        if (error == InventoryErrors.InvalidStateTransition)
            return InventoryReservationErrors.InvalidStateTransition;

        if (error == InventoryErrors.ReleaseExceedsReserved)
            return InventoryReservationErrors.ReleaseExceedsReserved;

        if (error == InventoryErrors.CommitExceedsReserved)
            return InventoryReservationErrors.CommitExceedsReserved;

        return InventoryReservationErrors.LifecycleFailed;
    }
}
