using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.Items["RequestId"] = requestId;
        context.Response.Headers["X-Request-ID"] = requestId;

        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString()
            : "none";

        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId!,
            ["RequestId"] = requestId
        }))
        {
            _logger.LogInformation("HTTP {Method} {Path} started", method, path);

            try
            {
                await _next(context);
            }
            catch (Exception)
            {
                stopwatch.Stop();
                _logger.LogError("HTTP {Method} {Path} failed in {Elapsed}ms", method, path, stopwatch.ElapsedMilliseconds);
                throw;
            }

            stopwatch.Stop();
            _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {Elapsed}ms",
                method, path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
    }
}
