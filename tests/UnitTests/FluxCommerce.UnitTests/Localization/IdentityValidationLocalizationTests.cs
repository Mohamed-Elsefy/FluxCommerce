using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.Implementation;
using FluxCommerce.BuildingBlocks.Localization.Resources;
using FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Localization;

public class IdentityValidationLocalizationTests
{
    private static ILocalizer CreateLocalizer() =>
        new SharedLocalizer(new StringLocalizer<SharedResources>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions()),
                NullLoggerFactory.Instance)));

    [Fact]
    public void RegisterCustomer_WithWeakPassword_ReturnsLocalizedMessage_ForFrenchCulture()
    {
        using var scope = WithCulture("fr");
        var validator = new RegisterCustomerCommandValidator(CreateLocalizer());

        var result = validator.Validate(new RegisterCustomerCommand("user@example.com", "weak", "John", "Doe"));

        Assert.False(result.IsValid);
        Assert.Contains("Le mot de passe doit contenir au moins 8 caractères.", result.Errors["Password"]);
    }

    [Fact]
    public void RegisterCustomer_WithWeakPassword_ReturnsEnglishMessage_ForDefaultCulture()
    {
        using var scope = WithCulture("en");
        var validator = new RegisterCustomerCommandValidator(CreateLocalizer());

        var result = validator.Validate(new RegisterCustomerCommand("user@example.com", "weak", "John", "Doe"));

        Assert.False(result.IsValid);
        Assert.Contains("Password must be at least 8 characters long.", result.Errors["Password"]);
    }

    [Fact]
    public void Login_WithoutPassword_ReturnsLocalizedMessage_ForFrenchCulture()
    {
        using var scope = WithCulture("fr");
        var validator = new LoginCommandValidator(CreateLocalizer());

        var result = validator.Validate(new LoginCommand("user@example.com", string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains("Le mot de passe est obligatoire.", result.Errors["Password"]);
    }

    [Fact]
    public void ChangePassword_WithMismatchedPasswords_ReturnsLocalizedMessage_ForFrenchCulture()
    {
        using var scope = WithCulture("fr");
        var validator = new ChangePasswordCommandValidator(CreateLocalizer());

        var result = validator.Validate(new ChangePasswordCommand("Current!Pass1", "New!Pass1", "Other!Pass1"));

        Assert.False(result.IsValid);
        Assert.Contains("Les mots de passe ne correspondent pas.", result.Errors["ConfirmPassword"]);
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