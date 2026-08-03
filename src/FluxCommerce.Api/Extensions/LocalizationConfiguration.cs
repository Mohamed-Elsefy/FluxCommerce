using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace FluxCommerce.Api.Extensions;

public static class LocalizationConfiguration
{
    private const string DefaultCulture = "en";

    private static readonly string[] SupportedCultures = ["en", "fr", "de", "es", "ar"];

    public static IServiceCollection AddLocalizationConfiguration(this IServiceCollection services)
    {
        services.AddLocalization();

        services.AddRequestLocalization(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(DefaultCulture);

            options.SupportedCultures = SupportedCultures
                .Select(culture => new CultureInfo(culture))
                .ToList();

            options.SupportedUICultures = SupportedCultures
                .Select(culture => new CultureInfo(culture))
                .ToList();

            options.ApplyCurrentCultureToResponseHeaders = true;

            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider()
            ];
        });

        return services;
    }
}
