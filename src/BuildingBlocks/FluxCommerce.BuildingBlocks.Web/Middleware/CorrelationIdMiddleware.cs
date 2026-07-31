using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private const string CorrelationIdItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var header)
            ? header.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[CorrelationIdItemKey] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        Activity.Current?.SetTag("correlation_id", correlationId);

        await _next(context);
    }
}
