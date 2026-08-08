using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Cart.Contracts.Requests;

/// <summary>
/// Request to add an item to a Guest Cart.
/// </summary>
public sealed record AddGuestCartItemRequest(
    [Required] Guid VariantId,
    [Required][Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal UnitPriceAmount,
    [Required][StringLength(3, MinimumLength = 3)] string Currency,
    [Required][Range(1, int.MaxValue)] int Quantity);

/// <summary>
/// Request to add an item to the authenticated user's Cart.
/// </summary>
public sealed record AddAuthenticatedCartItemRequest(
    [Required] Guid VariantId,
    [Required][Range(typeof(decimal), "0.01", "79228162514264337593543950335")] decimal UnitPriceAmount,
    [Required][StringLength(3, MinimumLength = 3)] string Currency,
    [Required][Range(1, int.MaxValue)] int Quantity);

/// <summary>
/// Request to update an item quantity in a Guest Cart.
/// </summary>
public sealed record UpdateGuestCartItemQuantityRequest(
    [Required] Guid VariantId,
    [Required][Range(1, int.MaxValue)] int Quantity);

/// <summary>
/// Request to update an item quantity in the authenticated user's Cart.
/// </summary>
public sealed record UpdateAuthenticatedCartItemQuantityRequest(
    [Required] Guid VariantId,
    [Required][Range(1, int.MaxValue)] int Quantity);

/// <summary>
/// Request to remove an item from a Guest Cart.
/// </summary>
public sealed record RemoveGuestCartItemRequest(
    [Required] Guid VariantId);

/// <summary>
/// Request to remove an item from the authenticated user's Cart.
/// </summary>
public sealed record RemoveAuthenticatedCartItemRequest(
    [Required] Guid VariantId);

/// <summary>
/// Request to merge a Guest Cart into the authenticated user's Cart.
/// </summary>
public sealed record MergeCartRequest(
    [Required] Guid GuestCartId);

/// <summary>
/// Request to create a new Guest Cart.
/// </summary>
public sealed record CreateGuestCartRequest;