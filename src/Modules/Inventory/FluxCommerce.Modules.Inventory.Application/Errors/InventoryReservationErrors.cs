using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Inventory.Application.Errors;

/// <summary>
/// Application-level errors raised by the Inventory reservation lifecycle use cases (reservation
/// lookup, lifecycle state transitions, insufficient stock and input validation).
/// </summary>
public static class InventoryReservationErrors
{
    public static readonly Error InvalidOrderId = Error.Validation(
        "Inventory.InvalidOrderId",
        "A valid order identifier is required.");

    public static readonly Error InvalidReservationId = Error.Validation(
        "Inventory.InvalidReservationId",
        "A valid reservation identifier is required.");

    public static readonly Error ReservationNotFound = Error.NotFound(
        "Inventory.ReservationNotFound",
        "The reservation was not found.");

    public static readonly Error InvalidExpiration = Error.Validation(
        "Inventory.InvalidExpiration",
        "A reservation expiration must be in the future.");

    public static readonly Error InsufficientStock = Error.Conflict(
        "Inventory.InsufficientStock",
        "There is not enough available stock to satisfy the requested quantity.");

    public static readonly Error InvalidStateTransition = Error.Conflict(
        "Inventory.InvalidStateTransition",
        "The reservation cannot transition to the requested state from its current state.");

    public static readonly Error ReleaseExceedsReserved = Error.Conflict(
        "Inventory.ReleaseExceedsReserved",
        "A release cannot return more quantity than is currently reserved.");

    public static readonly Error CommitExceedsReserved = Error.Conflict(
        "Inventory.CommitExceedsReserved",
        "A commit cannot consume more quantity than is currently reserved.");

    public static readonly Error LifecycleFailed = Error.Failure(
        "Inventory.ReservationLifecycleFailed",
        "The reservation operation failed.");
}
