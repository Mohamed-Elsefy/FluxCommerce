using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.Implementation;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.BuildingBlocks.Localization.DependencyInjection;

public static class LocalizationServiceExtensions
{
    public static IServiceCollection AddLocalizationServices(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentCulture, CurrentCulture>();
        services.AddSingleton<ILocalizer, SharedLocalizer>();

        return services;
    }
}