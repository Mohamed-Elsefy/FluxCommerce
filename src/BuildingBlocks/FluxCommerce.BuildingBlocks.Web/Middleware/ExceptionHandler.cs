using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.ProblemDetails;

namespace FluxCommerce.BuildingBlocks.Web.Middleware;

public sealed class ExceptionHandler(ILocalizer localizer) : IExceptionHandler
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

    private ProblemDetail MapException(Exception exception)
    {
        return exception switch
        {
            KeyNotFoundException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title = localizer["Errors.Title.NotFound"],
                Status = StatusCodes.Status404NotFound,
                Detail = exception.Message
            },

            UnauthorizedAccessException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                Title = localizer["Errors.Title.Unauthorized"],
                Status = StatusCodes.Status401Unauthorized,
                Detail = exception.Message
            },

            InvalidOperationException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                Title = localizer["Errors.Title.Conflict"],
                Status = StatusCodes.Status409Conflict,
                Detail = exception.Message
            },

            ArgumentException => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = localizer["Errors.Title.BadRequest"],
                Status = StatusCodes.Status400BadRequest,
                Detail = exception.Message
            },

            _ => new ProblemDetail
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = localizer["Errors.Title.InternalServerError"],
                Status = StatusCodes.Status500InternalServerError,
                Detail = localizer["Errors.UnhandledException"]
            }
        };
    }
}