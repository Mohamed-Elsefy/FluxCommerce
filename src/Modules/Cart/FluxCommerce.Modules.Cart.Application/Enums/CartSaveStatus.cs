namespace FluxCommerce.Modules.Cart.Application.Enums;

public enum CartSaveStatus
{
    Success = 0,
    VersionConflict = 1,
    CartMissing = 2,
    DuplicateActiveCart = 3,
}
