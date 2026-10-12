namespace FluxCommerce.Api.Constants;

/// <summary>
/// Swagger tag names for the API surface. Owned by the host (not a generic web
/// primitive) because the values enumerate this API's module areas.
/// </summary>
public static class EndpointTags
{
    public const string Catalog = "Catalog";
    public const string Cart = "Cart";
    public const string Orders = "Orders";
    public const string Inventory = "Inventory";
    public const string Payments = "Payments";
    public const string Identity = "Identity";
    public const string Dashboard = "Dashboard";
    public const string Health = "Health";
}
