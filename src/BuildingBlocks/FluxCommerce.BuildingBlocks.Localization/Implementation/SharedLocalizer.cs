using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.Resources;
using Microsoft.Extensions.Localization;

namespace FluxCommerce.BuildingBlocks.Localization.Implementation;

public sealed class SharedLocalizer(IStringLocalizer<SharedResources> localizer) : ILocalizer
{
    public string this[string key] => Get(key);

    public string Get(string key, params object[] arguments) =>
        arguments.Length == 0
            ? localizer[key].Value
            : localizer[key, arguments].Value;
}