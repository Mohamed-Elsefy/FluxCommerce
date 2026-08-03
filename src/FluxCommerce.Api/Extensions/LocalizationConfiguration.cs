using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;

namespace FluxCommerce.Api.Extensions;

public static class LocalizationConfiguration
{
    public const string DefaultCulture = "en";

    public static readonly string[] SupportedCultures = ["en", "fr", "de", "es", "ar"];

    public static IServiceCollection AddLocalizationConfiguration(this IServiceCollection services)
    {
        services.AddLocalization();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(DefaultCulture);
            options.AddSupportedCultures(SupportedCultures);
            options.AddSupportedUICultures(SupportedCultures);
            options.ApplyCurrentCultureToResponseHeaders = true;

            options.RequestCultureProviders.Clear();
            options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
            options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
            options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
        });

        return services;
    }
}
