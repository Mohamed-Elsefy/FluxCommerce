using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Inventory.Contracts.Requests;

/// <summary>
/// Request to initialize an inventory item for a SKU. An initial on-hand quantity of zero is valid
/// (the SKU can be registered before stock arrives); negative quantities are rejected. The SKU is
/// normalized (trimmed, uppercased) by the Inventory domain before it is stored.
/// </summary>
public sealed record CreateInventoryItemRequest(
    [Required(AllowEmptyStrings = false), StringLength(64)] string Sku,
    [Range(0, int.MaxValue)] int OnHandQuantity);

/// <summary>
/// Request to add stock to an existing inventory item. Adjustment is additive only — stock
/// consumption is modeled by the reservation lifecycle, so there is no unsafe manual decrement path.
/// </summary>
public sealed record AdjustStockRequest(
    [Range(1, int.MaxValue)] int Quantity);
