using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;

namespace FluxCommerce.Api.Localization;

public sealed class ErrorCodeLocalizer(ILocalizer localizer) : IErrorCodeLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> CodeToKey = new Dictionary<string, string>
    {
        ["Identity.ValidationFailed"] = "Errors.RequestValidation",
        ["Identity.InvalidCredentials"] = "Authentication.Login.InvalidCredentials",
        ["Identity.EmailNotVerified"] = "Authentication.Login.EmailNotConfirmed",
        ["Identity.AccountLocked"] = "Authentication.Login.LockedOut",
        ["Identity.InvalidToken"] = "Errors.InvalidToken",
        ["Identity.DuplicateEmail"] = "Authentication.Register.EmailAlreadyExists",
        ["Identity.InvalidEmail"] = "Validation.Email.Invalid",
        ["Identity.Unauthenticated"] = "Authorization.Unauthorized",
        ["Identity.UserNotFound"] = "Errors.NotFound",
        ["Identity.EmailSendFailed"] = "Errors.EmailSendFailed",
        ["Identity.RoleAlreadyAssigned"] = "Errors.RoleAlreadyAssigned",
        ["Catalog.ValidationFailed"] = "Errors.RequestValidation",
        ["Catalog.DuplicateCategoryName"] = "Catalog.DuplicateCategoryName",
        ["Catalog.DuplicateCategorySlug"] = "Catalog.DuplicateCategorySlug",
        ["Catalog.CategoryNotFound"] = "Errors.NotFound",
        ["Catalog.ProductNotFound"] = "Errors.NotFound",
        ["Catalog.DuplicateProductName"] = "Catalog.DuplicateProductName",
        ["Catalog.DuplicateProductSlug"] = "Catalog.DuplicateProductSlug",
        ["Catalog.DeletedCategoryCannotBeUsed"] = "Catalog.DeletedCategoryCannotBeUsed",
        ["Catalog.DeletedProductCannotBeModified"] = "Catalog.DeletedProductCannotBeModified",
        ["Catalog.InvalidProductTransition"] = "Catalog.InvalidProductTransition",
        ["Catalog.InvalidSeoMetadata"] = "Catalog.InvalidSeoMetadata",
        ["Catalog.ParentCategoryNotFound"] = "Catalog.ParentCategoryNotFound",
        ["Catalog.ParentCategoryDeleted"] = "Catalog.ParentCategoryDeleted",
        ["Catalog.DeletedCategoryCannotBeModified"] = "Catalog.DeletedCategoryCannotBeModified",
        ["Catalog.DeletedVariantCannotBeModified"] = "Catalog.DeletedVariantCannotBeModified",
        ["Catalog.VariantNotFound"] = "Catalog.VariantNotFound",
        ["Catalog.DuplicateVariantSku"] = "Catalog.DuplicateVariantSku",
        ["Catalog.InvalidSku"] = "Catalog.InvalidSku",
        ["Catalog.CannotBeOwnParent"] = "Catalog.CannotBeOwnParent",
        ["Catalog.InvalidSlug"] = "Catalog.InvalidSlug",
        ["Catalog.AttributeDefinitionNotFound"] = "Catalog.AttributeDefinitionNotFound",
        ["Catalog.DuplicateAttributeName"] = "Catalog.DuplicateAttributeName",
        ["Catalog.DuplicateAttributeSlug"] = "Catalog.DuplicateAttributeSlug",
        ["Catalog.DeletedAttributeDefinitionCannotBeModified"] = "Catalog.DeletedAttributeDefinitionCannotBeModified",
        ["Catalog.AttributeValueRequired"] = "Catalog.AttributeValueRequired",
        ["Catalog.AttributeValueTooLong"] = "Catalog.AttributeValueTooLong",
        ["Catalog.InvalidAttributeValue"] = "Catalog.InvalidAttributeValue",
        ["Catalog.DuplicateProductAttributeValue"] = "Catalog.DuplicateProductAttributeValue",
        ["Catalog.ProductAttributeValueNotFound"] = "Catalog.ProductAttributeValueNotFound",
        ["Catalog.DeletedAttributeDefinitionCannotBeUsed"] = "Catalog.DeletedAttributeDefinitionCannotBeUsed",
        ["Catalog.AttributeDefinitionNotActive"] = "Catalog.AttributeDefinitionNotActive",
        ["Catalog.AttributeDefinitionCategoryMismatch"] = "Catalog.AttributeDefinitionCategoryMismatch",
        ["Catalog.SpecificationNameRequired"] = "Catalog.SpecificationNameRequired",
        ["Catalog.SpecificationNameTooLong"] = "Catalog.SpecificationNameTooLong",
        ["Catalog.SpecificationValueRequired"] = "Catalog.SpecificationValueRequired",
        ["Catalog.SpecificationValueTooLong"] = "Catalog.SpecificationValueTooLong",
        ["Catalog.DuplicateProductSpecification"] = "Catalog.DuplicateProductSpecification",
        ["Catalog.ProductSpecificationNotFound"] = "Catalog.ProductSpecificationNotFound",
        ["Catalog.ImageUrlRequired"] = "Catalog.ImageUrlRequired",
        ["Catalog.ImageUrlTooLong"] = "Catalog.ImageUrlTooLong",
        ["Catalog.AltTextTooLong"] = "Catalog.AltTextTooLong",
        ["Catalog.ProductImageNotFound"] = "Catalog.ProductImageNotFound",
        ["Catalog.InvalidImageOrder"] = "Catalog.InvalidImageOrder",
        ["Cart.InvalidQuantity"] = "Cart.InvalidQuantity",
        ["Cart.QuantityLimitExceeded"] = "Cart.QuantityLimitExceeded",
        ["Cart.CartItemNotFound"] = "Cart.CartItemNotFound",
        ["Cart.CartNotFound"] = "Cart.CartNotFound",
        ["Cart.CartAccessDenied"] = "Cart.CartAccessDenied",
        ["Cart.CartConcurrencyConflict"] = "Cart.CartConcurrencyConflict",
        ["Cart.InvalidCartItem"] = "Cart.InvalidCartItem",
        ["Cart.InvalidCartId"] = "Cart.InvalidCartId",
        ["Cart.AuthenticationRequired"] = "Cart.AuthenticationRequired",
        ["Cart.VariantNotFound"] = "Cart.VariantNotFound",
        ["Cart.VariantUnavailable"] = "Cart.VariantUnavailable",
        ["Cart.CatalogOperationFailed"] = "Cart.CatalogOperationFailed",
        ["Order.CartEmpty"] = "Order.CartEmpty",
        ["Order.VariantUnavailable"] = "Order.VariantUnavailable",
        ["Order.DuplicateVariant"] = "Order.DuplicateVariant",
        ["Order.CurrencyMismatch"] = "Order.CurrencyMismatch",
        ["Order.AuthenticationRequired"] = "Order.AuthenticationRequired",
        ["Order.CartNotFound"] = "Order.CartNotFound",
        ["Order.CartAccessDenied"] = "Order.CartAccessDenied",
        ["Order.InvalidIdempotencyKey"] = "Order.InvalidIdempotencyKey",
        ["Order.IdempotencyConflict"] = "Order.IdempotencyConflict",
        ["Order.InvalidAddress"] = "Order.InvalidAddress",
        ["Order.QueryAuthenticationRequired"] = "Authorization.Unauthorized",
        ["Order.ValidationFailed"] = "Errors.RequestValidation",
        ["Order.NotFound"] = "Errors.NotFound",
        ["Order.CancelConflict"] = "Order.CancelConflict",
        ["Inventory.InvalidSku"] = "Inventory.InvalidSku",
        ["Inventory.InvalidQuantity"] = "Inventory.InvalidQuantity",
        ["Inventory.SkuNotFound"] = "Errors.NotFound",
        ["Inventory.DuplicateSku"] = "Inventory.DuplicateSku",
        ["Inventory.InvalidReservationId"] = "Inventory.InvalidReservationId",
        ["Inventory.ReservationNotFound"] = "Errors.NotFound",
    };

    public string Localize(string code, string fallback)
    {
        if (!CodeToKey.TryGetValue(code, out var key))
            return fallback;

        var value = localizer.Get(key);

        return value == key ? fallback : value;
    }
}