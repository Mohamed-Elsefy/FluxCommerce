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
        ["Catalog.CannotBeOwnParent"] = "Catalog.CannotBeOwnParent",
        ["Catalog.InvalidSlug"] = "Catalog.InvalidSlug",
    };

    public string Localize(string code, string fallback)
    {
        if (!CodeToKey.TryGetValue(code, out var key))
            return fallback;

        var value = localizer.Get(key);

        return value == key ? fallback : value;
    }
}