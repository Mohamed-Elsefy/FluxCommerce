using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Inventory.Application.Errors;

/// <summary>
/// Application-level errors raised by the Inventory stock-management use cases (SKU validation,
/// duplicate-SKU conflicts, missing inventory and invalid quantity adjustments).
/// </summary>
public static class InventoryApplicationErrors
{
    public static readonly Error InvalidSku = Error.Validation(
        "Inventory.InvalidSku",
        "The SKU is invalid.");

    public static readonly Error InvalidQuantity = Error.Validation(
        "Inventory.InvalidQuantity",
        "An inventory quantity must be a positive integer.");

    public static readonly Error SkuNotFound = Error.NotFound(
        "Inventory.SkuNotFound",
        "No inventory item exists for the specified SKU.");

    public static readonly Error DuplicateSku = Error.Conflict(
        "Inventory.DuplicateSku",
        "An inventory item with this SKU already exists.");

    public static readonly Error ValidationFailed = Error.Validation(
        "Inventory.ValidationFailed",
        "The inventory query parameters are invalid.");
}
