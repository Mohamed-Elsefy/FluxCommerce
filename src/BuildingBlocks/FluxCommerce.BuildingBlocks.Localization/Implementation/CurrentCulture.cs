using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.BuildingBlocks.Localization.Implementation;

public sealed class CurrentCulture : ICurrentCulture
{
    public string Name => CultureInfo.CurrentCulture.Name;

    public string UiCultureName => CultureInfo.CurrentUICulture.Name;
}