namespace FluxCommerce.BuildingBlocks.Localization.Abstractions;

/// <summary>
/// Provides access to the culture resolved for the current execution context.
/// </summary>
public interface ICurrentCulture
{
    /// <summary>
    /// Gets the name of the current culture (formatting), e.g. "en".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the name of the current UI culture (display language), e.g. "fr".
    /// </summary>
    string UiCultureName { get; }
}
