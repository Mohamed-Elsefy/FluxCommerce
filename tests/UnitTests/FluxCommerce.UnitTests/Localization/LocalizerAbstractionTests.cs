using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.DependencyInjection;
using FluxCommerce.BuildingBlocks.Localization.Implementation;
using FluxCommerce.BuildingBlocks.Localization.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Localization;

public class LocalizerAbstractionTests
{
    private static ILocalizer CreateLocalizer() =>
        new SharedLocalizer(new StringLocalizer<SharedResources>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions()),
                NullLoggerFactory.Instance)));

    [Fact]
    public void SharedLocalizer_ReturnsLocalizedValue_ForExistingKey()
    {
        using var scope = WithCulture("en");
        var localizer = CreateLocalizer();

        Assert.Equal("Save", localizer["Common.Save"]);
    }

    [Fact]
    public void SharedLocalizer_ReturnsKeyItself_ForMissingKey()
    {
        using var scope = WithCulture("en");

        var value = CreateLocalizer()["Does.Not.Exist"];

        Assert.Equal("Does.Not.Exist", value);
    }

    [Fact]
    public void SharedLocalizer_FormatsPlaceholders()
    {
        using var scope = WithCulture("en");

        var value = CreateLocalizer().Get("Validation.Field.Required", "Email");

        Assert.Equal("Email is required.", value);
    }

    [Fact]
    public void SharedLocalizer_ResolvesValue_FromCurrentUiCulture()
    {
        using var scope = WithCulture("fr");

        var value = CreateLocalizer()["Common.Save"];

        Assert.Equal("Enregistrer", value);
    }

    [Fact]
    public void CurrentCulture_ExposesResolvedCultureAndUiCulture()
    {
        using var scope = WithCulture("fr");

        var currentCulture = new CurrentCulture();

        Assert.Equal("fr", currentCulture.Name);
        Assert.Equal("fr", currentCulture.UiCultureName);
    }

    [Fact]
    public void AddLocalizationServices_RegistersAbstractionsAndImplementations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();

        var localizer = provider.GetRequiredService<ILocalizer>();
        var currentCulture = provider.GetRequiredService<ICurrentCulture>();

        Assert.IsType<SharedLocalizer>(localizer);
        Assert.IsType<CurrentCulture>(currentCulture);
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
