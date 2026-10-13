namespace FluxCommerce.BuildingBlocks.Web.Extensions;

/// <summary>
/// Shared HttpContext item keys and response header names for request correlation.
/// Centralized so the correlation and logging middlewares cannot drift apart.
/// </summary>
public static class HttpContextItems
{
    public const string CorrelationId = "CorrelationId";

    public const string CorrelationIdHeader = "X-Correlation-ID";

    public const string RequestId = "RequestId";

    public const string RequestIdHeader = "X-Request-ID";
}
