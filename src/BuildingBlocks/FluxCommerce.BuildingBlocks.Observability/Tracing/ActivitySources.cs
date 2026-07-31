using System.Diagnostics;

namespace FluxCommerce.BuildingBlocks.Observability.Tracing;

public static class ActivitySources
{
    public const string FluxCommerce = "FluxCommerce";

    public static readonly ActivitySource Platform = new(FluxCommerce);
}
