namespace FluxCommerce.BuildingBlocks.Localization.Abstractions;

/// <summary>
/// Framework-independent localization service over the shared resources.
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// Gets the localized string for the specified resource key in the current UI culture.
    /// </summary>
    /// <param name="key">The dot-namespaced resource key (e.g. <c>Validation.Email.Required</c>).</param>
    /// <returns>The localized string; the key itself when the key is missing.</returns>
    string this[string key] { get; }

    /// <summary>
    /// Gets the localized string for the specified resource key, formatting placeholders in the current UI culture.
    /// </summary>
    /// <param name="key">The dot-namespaced resource key (e.g. <c>Validation.Field.Required</c>).</param>
    /// <param name="arguments">Format arguments for the message placeholders ({0}, {1}, ...).</param>
    /// <returns>The localized string; the key itself when the key is missing.</returns>
    string Get(string key, params object[] arguments);
}
