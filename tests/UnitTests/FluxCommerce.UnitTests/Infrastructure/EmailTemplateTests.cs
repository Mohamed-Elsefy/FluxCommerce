using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.Implementation;
using FluxCommerce.BuildingBlocks.Localization.Resources;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class EmailTemplateTests
{
    private static EmailOptions CreateOptions(string frontendBaseUrl = "https://localhost:4200") =>
        new()
        {
            Host = "localhost",
            Port = 1025,
            FromEmail = "noreply@fluxcommerce.local",
            FromName = "FluxCommerce",
            FrontendBaseUrl = frontendBaseUrl
        };

    private static ILocalizer CreateLocalizer() =>
        new SharedLocalizer(new StringLocalizer<SharedResources>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions()),
                NullLoggerFactory.Instance)));

    [Fact]
    public void Verification_Template_ContainsVerifyUrlWithToken()
    {
        using var scope = WithCulture("en");
        var message = EmailTemplates.Verification(CreateLocalizer(), CreateOptions(), "user@example.com", "tok-123");

        Assert.Equal("user@example.com", message.ToEmail);
        Assert.Equal("Verify your FluxCommerce account", message.Subject);
        Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.HtmlBody);
        Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.TextBody);
        Assert.Contains("24 hours", message.HtmlBody);
    }

    [Fact]
    public void Verification_Template_EscapesTokenAndNormalizesBaseUrl()
    {
        using var scope = WithCulture("en");
        var message = EmailTemplates.Verification(
            CreateLocalizer(),
            CreateOptions("https://localhost:4200/"),
            "user@example.com",
            "a+b/c==");

        Assert.Contains("https://localhost:4200/verify-email?token=a%2Bb%2Fc%3D%3D", message.HtmlBody);
    }

    [Fact]
    public void PasswordReset_Template_ContainsResetUrlWithToken()
    {
        using var scope = WithCulture("en");
        var message = EmailTemplates.PasswordReset(CreateLocalizer(), CreateOptions(), "user@example.com", "reset-456");

        Assert.Equal("user@example.com", message.ToEmail);
        Assert.Equal("Reset your FluxCommerce password", message.Subject);
        Assert.Contains("https://localhost:4200/reset-password?token=reset-456", message.HtmlBody);
        Assert.Contains("https://localhost:4200/reset-password?token=reset-456", message.TextBody);
        Assert.Contains("1 hour", message.HtmlBody);
    }

    [Fact]
    public void Verification_Template_IsLocalized_ForFrenchCulture()
    {
        using var scope = WithCulture("fr");
        var message = EmailTemplates.Verification(CreateLocalizer(), CreateOptions(), "user@example.com", "tok-123");

        Assert.Equal("Vérifiez votre compte FluxCommerce", message.Subject);
        Assert.Contains("lang=\"fr\"", message.HtmlBody);
        Assert.Contains("Vérifiez votre adresse e-mail", message.HtmlBody);
        Assert.Contains("Vérifier l'e-mail", message.HtmlBody);
        Assert.Contains("Vérifiez votre compte FluxCommerce", message.TextBody);
    }

    [Fact]
    public void PasswordReset_Template_IsLocalized_ForGermanCulture()
    {
        using var scope = WithCulture("de");
        var message = EmailTemplates.PasswordReset(CreateLocalizer(), CreateOptions(), "user@example.com", "reset-456");

        Assert.Equal("Setzen Sie Ihr FluxCommerce-Passwort zurück", message.Subject);
        Assert.Contains("lang=\"de\"", message.HtmlBody);
        Assert.Contains("Passwort zurücksetzen", message.HtmlBody);
        Assert.Contains("Setzen Sie Ihr FluxCommerce-Passwort zurück", message.TextBody);
    }

    [Fact]
    public void PasswordReset_Template_IsLocalized_ForArabicCulture()
    {
        using var scope = WithCulture("ar");
        var message = EmailTemplates.PasswordReset(CreateLocalizer(), CreateOptions(), "user@example.com", "reset-456");

        Assert.Equal("أعد تعيين كلمة مرور FluxCommerce", message.Subject);
        Assert.Contains("lang=\"ar\"", message.HtmlBody);
        Assert.Contains("أعد تعيين كلمة المرور", message.HtmlBody);
    }

    [Fact]
    public void Verification_Template_ResolvesUrlPlaceholders_ForEveryCulture()
    {
        foreach (var culture in new[] { "en", "fr", "de", "es", "ar" })
        {
            using var scope = WithCulture(culture);
            var message = EmailTemplates.Verification(CreateLocalizer(), CreateOptions(), "user@example.com", "tok-123");

            Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.HtmlBody);
            Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.TextBody);
        }
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
