using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using FluxCommerce.BuildingBlocks.Web.ProblemDetails;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class ExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetail = MapException(exception);
        problemDetail.Instance = httpContext.Request.Path;
        problemDetail.TraceId = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problemDetail.Status;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetail, cancellationToken);

        return true;
    }

    private static ProblemDetail MapException(Exception exception)
    {
        return exception switch
        {
            KeyNotFoundException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = "Not Found",
                Status = StatusCodes.Status404NotFound,
                Detail = exception.Message
            },

            UnauthorizedAccessException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = exception.Message
            },

            InvalidOperationException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = exception.Message
            },

            ArgumentException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = exception.Message
            },

            _ => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred. Please try again later."
            }
        };
    }
}
