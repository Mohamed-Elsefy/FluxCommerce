using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Web.Extensions;
using Microsoft.AspNetCore.Http;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HttpContextItems.CorrelationIdHeader, out var header)
            ? header.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[HttpContextItems.CorrelationId] = correlationId;
        context.Response.Headers[HttpContextItems.CorrelationIdHeader] = correlationId;

        Activity.Current?.SetTag("correlation_id", correlationId);

        await _next(context);
    }
}
