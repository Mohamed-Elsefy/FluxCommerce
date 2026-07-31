using Microsoft.AspNetCore.Http;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public static class HttpContextExtensions
{
    public static string? TryGetCorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue("CorrelationId", out var id)
            ? id?.ToString()
            : null;
    }

    public static string GetCorrelationId(this HttpContext context)
    {
        return context.TryGetCorrelationId() ?? "none";
    }

    public static string? TryGetRequestId(this HttpContext context)
    {
        return context.Items.TryGetValue("RequestId", out var id)
            ? id?.ToString()
            : null;
    }

    public static string GetRequestId(this HttpContext context)
    {
        return context.TryGetRequestId() ?? "none";
    }
}
