using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;

namespace FluxCommerce.Api.Localization;

/// <summary>
/// Maps stable <c>Error.Code</c> values to localization resource keys at the
/// presentation boundary. Per-area entries live in sibling partial files so the
/// map grows by area, not by editing one god dictionary.
/// </summary>
public sealed partial class ErrorCodeLocalizer(ILocalizer localizer) : IErrorCodeLocalizer
{
    // Lazy: the per-area entry tables are static fields declared in sibling partial
    // files, and static field initialization order across partials is not guaranteed.
    // Deferring the merge to first use guarantees every table is assigned.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CodeToKey = new(BuildMap);

    public string Localize(string code, string fallback)
    {
        if (!CodeToKey.Value.TryGetValue(code, out var key))
            return fallback;

        var value = localizer.Get(key);

        return value == key ? fallback : value;
    }

    private static IReadOnlyDictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        AddEntries(map, IdentityEntries);
        AddEntries(map, CatalogEntries);
        AddEntries(map, CartEntries);
        AddEntries(map, OrderEntries);
        AddEntries(map, InventoryEntries);
        return map;
    }

    private static void AddEntries(
        Dictionary<string, string> map,
        IReadOnlyDictionary<string, string> entries)
    {
        foreach (var entry in entries)
            map[entry.Key] = entry.Value;
    }
}
