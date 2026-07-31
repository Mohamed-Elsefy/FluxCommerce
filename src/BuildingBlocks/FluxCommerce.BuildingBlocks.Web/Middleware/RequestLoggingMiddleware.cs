using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class RequestLoggingMiddleware
{
    private const string RequestIdHeader = "X-Request-ID";
    private const string RequestIdItemKey = "RequestId";
    private const string CorrelationIdItemKey = "CorrelationId";

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
        context.Items[RequestIdItemKey] = requestId;
        context.Response.Headers[RequestIdHeader] = requestId;

        var correlationId = context.Items.TryGetValue(CorrelationIdItemKey, out var cid)
            ? cid?.ToString()
            : "none";

        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString() ?? "none";
        var spanId = activity?.SpanId.ToString() ?? "none";

        Activity.Current?.SetTag("http.request.id", requestId);

        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId!,
            ["RequestId"] = requestId,
            ["TraceId"] = traceId,
            ["SpanId"] = spanId
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
