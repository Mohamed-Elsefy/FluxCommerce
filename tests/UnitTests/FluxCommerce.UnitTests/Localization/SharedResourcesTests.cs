using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Localization;

public class SharedResourcesTests
{
    private static readonly string[] AllKeys =
    [
        "Common.Success",
        "Common.Error",
        "Common.Warning",
        "Common.Save",
        "Common.Cancel",
        "Common.Confirm",
        "Common.Delete",
        "Common.Search",
        "Common.Language",
        "Common.Loading",
        "Common.NoResults",
        "Common.Unknown",
        "Validation.Field.Required",
        "Validation.Field.Invalid",
        "Validation.Field.MinLength",
        "Validation.Field.MaxLength",
        "Validation.Field.Range",
        "Validation.Email.Required",
        "Validation.Email.Invalid",
        "Validation.Email.MaxLength",
        "Validation.Password.TooWeak",
        "Validation.Password.Mismatch",
        "Validation.Password.Required",
        "Validation.Password.MinLength",
        "Validation.Password.MaxLength",
        "Validation.Password.Uppercase",
        "Validation.Password.Lowercase",
        "Validation.Password.Digit",
        "Validation.Password.Special",
        "Validation.Fields.FirstName",
        "Validation.Fields.LastName",
        "Validation.Fields.Token",
        "Validation.Fields.RefreshToken",
        "Validation.Fields.CurrentPassword",
        "Authentication.Login.InvalidCredentials",
        "Authentication.Login.LockedOut",
        "Authentication.Login.EmailNotConfirmed",
        "Authentication.Register.EmailAlreadyExists",
        "Authentication.VerifyEmail.InvalidToken",
        "Authentication.VerifyEmail.Success",
        "Authentication.PasswordReset.InvalidToken",
        "Authentication.PasswordReset.Success",
        "Authorization.Unauthorized",
        "Authorization.Forbidden",
        "Authorization.RoleRequired",
        "Authorization.ResourceOwner",
        "Errors.UnhandledException",
        "Errors.RequestValidation",
        "Errors.NotFound",
        "Errors.Conflict",
        "Errors.Cancelled",
        "Errors.OperationFailed",
        "Errors.InvalidToken",
        "Errors.EmailSendFailed",
        "Errors.RoleAlreadyAssigned",
        "Errors.Title.ValidationFailed",
        "Errors.Title.NotFound",
        "Errors.Title.Conflict",
        "Errors.Title.Unauthorized",
        "Errors.Title.Forbidden",
        "Errors.Title.BadRequest",
        "Errors.Title.InternalServerError",
        "Email.Verification.Subject",
        "Email.Verification.Heading",
        "Email.Verification.Body",
        "Email.Verification.TextIntro",
        "Email.Verification.ButtonText",
        "Email.Verification.ValidityNote",
        "Email.PasswordReset.Subject",
        "Email.PasswordReset.Heading",
        "Email.PasswordReset.Body",
        "Email.PasswordReset.TextIntro",
        "Email.PasswordReset.ButtonText",
        "Email.PasswordReset.ValidityNote",
        "Email.CopyLinkInstruction",
        "Validation.Fields.Sku",
        "Validation.Fields.VariantId",
        "Validation.Fields.VariantName",
        "Validation.Fields.AttributeName",
        "Validation.Fields.AttributeId",
        "Validation.Fields.DataType",
        "Validation.Fields.AttributeValue",
        "Catalog.VariantNotFound",
        "Catalog.DuplicateVariantSku",
        "Catalog.InvalidSku",
        "Catalog.DeletedVariantCannotBeModified",
        "Catalog.AttributeDefinitionNotFound",
        "Catalog.DuplicateAttributeName",
        "Catalog.DuplicateAttributeSlug",
        "Catalog.DeletedAttributeDefinitionCannotBeModified",
        "Catalog.AttributeValueRequired",
        "Catalog.AttributeValueTooLong",
        "Catalog.InvalidAttributeValue",
        "Catalog.DuplicateProductAttributeValue",
        "Catalog.ProductAttributeValueNotFound",
        "Catalog.DeletedAttributeDefinitionCannotBeUsed",
        "Catalog.AttributeDefinitionNotActive",
        "Catalog.AttributeDefinitionCategoryMismatch",
    ];

    private static readonly string[] SatelliteCultures = ["fr", "de", "es", "ar"];

    private static IStringLocalizer CreateLocalizer() =>
        new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            NullLoggerFactory.Instance).Create(typeof(SharedResources));

    [Fact]
    public void EveryDefinedKey_IsResolved_FromEnglishResource()
    {
        using var scope = WithCulture("en");
        var localizer = CreateLocalizer();

        foreach (var key in AllKeys)
        {
            var value = localizer[key];

            Assert.False(string.IsNullOrWhiteSpace(value.Value), $"Key '{key}' has an empty value.");
            Assert.NotEqual(key, value.Value);
        }
    }

    [Fact]
    public void EveryDefinedKey_IsResolved_InEverySatellite()
    {
        foreach (var culture in SatelliteCultures)
        {
            using var scope = WithCulture(culture);
            var localizer = CreateLocalizer();

            foreach (var key in AllKeys)
            {
                var value = localizer[key];

                Assert.False(
                    string.IsNullOrWhiteSpace(value.Value),
                    $"Key '{key}' has an empty value in '{culture}'.");
                Assert.NotEqual(key, value.Value);
            }
        }
    }

    [Theory]
    [InlineData("fr", "Common.Save", "Enregistrer")]
    [InlineData("de", "Common.Save", "Speichern")]
    [InlineData("es", "Common.Save", "Guardar")]
    [InlineData("ar", "Common.Save", "حفظ")]
    [InlineData("fr", "Authentication.Login.InvalidCredentials", "Adresse e-mail ou mot de passe incorrect.")]
    [InlineData("de", "Authentication.Login.InvalidCredentials", "E-Mail-Adresse oder Passwort ist falsch.")]
    [InlineData("es", "Authorization.Forbidden", "No tiene permiso para realizar esta acción.")]
    [InlineData("ar", "Authorization.Forbidden", "ليس لديك إذن لتنفيذ هذا الإجراء.")]
    [InlineData("fr", "Errors.NotFound", "La ressource demandée est introuvable.")]
    [InlineData("de", "Errors.NotFound", "Die angeforderte Ressource wurde nicht gefunden.")]
    [InlineData("es", "Errors.NotFound", "No se encontró el recurso solicitado.")]
    [InlineData("ar", "Errors.NotFound", "لم يتم العثور على المورد المطلوب.")]
    [InlineData("fr", "Email.Verification.Subject", "Vérifiez votre compte FluxCommerce")]
    [InlineData("de", "Email.PasswordReset.ButtonText", "Passwort zurücksetzen")]
    [InlineData("es", "Email.Verification.ButtonText", "Verificar correo electrónico")]
    [InlineData("ar", "Email.Verification.ValidityNote", "هذا الرابط صالح لمدة 24 ساعة. إذا لم تنشئ هذا الحساب، يمكنك تجاهل هذا البريد بأمان.")]
    public void SatelliteTranslations_AreResolvedPerCulture(string culture, string key, string expected)
    {
        using var scope = WithCulture(culture);
        var localizer = CreateLocalizer();

        var value = localizer[key];

        Assert.Equal(expected, value.Value);
    }

    [Theory]
    [InlineData("fr", "Email est obligatoire.")]
    [InlineData("de", "Email ist erforderlich.")]
    [InlineData("es", "Email es obligatorio.")]
    [InlineData("ar", "Email مطلوب.")]
    public void SatelliteTranslations_FormatPlaceholders(string culture, string expected)
    {
        using var scope = WithCulture(culture);
        var localizer = CreateLocalizer();

        var value = localizer["Validation.Field.Required", "Email"];

        Assert.Equal(expected, value.Value);
    }

    [Fact]
    public void SpecificCulture_FallsBackToEnglish()
    {
        using var scope = WithCulture("en-GB");

        var value = CreateLocalizer()["Common.Save"];

        Assert.Equal("Save", value.Value);
    }

    [Theory]
    [InlineData("zh")]
    [InlineData("it")]
    public void UnsupportedCulture_FallsBackToEnglish(string culture)
    {
        using var scope = WithCulture(culture);

        var value = CreateLocalizer()["Common.Save"];

        Assert.Equal("Save", value.Value);
    }

    [Fact]
    public void MissingKey_ReturnsKeyItself()
    {
        using var scope = WithCulture("en");

        var value = CreateLocalizer()["Common.DoesNotExist"];

        Assert.True(value.ResourceNotFound);
        Assert.Equal("Common.DoesNotExist", value.Value);
    }

    private static CultureScope WithCulture(string culture)
    {
        var scope = new CultureScope();
        scope.Apply(culture);
        return scope;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public void Apply(string culture)
        {
            var cultureInfo = new CultureInfo(culture);
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}