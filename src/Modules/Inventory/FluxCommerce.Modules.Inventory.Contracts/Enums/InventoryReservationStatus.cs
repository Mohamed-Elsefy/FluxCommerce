namespace FluxCommerce.Modules.Inventory.Contracts.Enums;

/// <summary>
/// Contract-level reservation status mirror of the Inventory domain state machine. Independent of the
/// Domain enum so inter-module consumers never depend on Inventory internals.
/// </summary>
public enum InventoryReservationStatus
{
    /// <summary>The reservation is active and holds its quantity.</summary>
    Pending = 0,

    /// <summary>The reserved quantity was consumed by the order.</summary>
    Committed = 1,

    /// <summary>The reservation was released and its quantity returned.</summary>
    Released = 2,

    /// <summary>The reservation expired and its quantity was returned.</summary>
    Expired = 3,
}
