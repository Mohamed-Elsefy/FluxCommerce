using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Web.ProblemDetails;
using FluxCommerce.BuildingBlocks.Web.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.BuildingBlocks.Web.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Success<T>(Result<T> result)
    {
        return result.IsSuccess
            ? Ok(ApiResponse<T>.Create(result.Value))
            : ErrorResult(result.Error);
    }

    protected IActionResult Success(Result result)
    {
        return result.IsSuccess
            ? Ok(ApiResponse.Ok())
            : ErrorResult(result.Error);
    }

    protected IActionResult Created<T>(Result<T> result, string? location = null)
    {
        if (result.IsFailure)
            return ErrorResult(result.Error);

        return string.IsNullOrWhiteSpace(location)
            ? CreatedAtAction(null, ApiResponse<T>.Create(result.Value))
            : Created(location, ApiResponse<T>.Create(result.Value));
    }

    protected IActionResult NoContent(Result result)
    {
        return result.IsSuccess
            ? NoContent()
            : ErrorResult(result.Error);
    }

    protected IActionResult Invalid(Dictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetail
        {
            Instance = Request?.Path,
            TraceId = HttpContext?.TraceIdentifier,
            Errors = errors
        };

        return BadRequest(problem);
    }

    private IActionResult ErrorResult(Error error)
    {
        var (statusCode, problem) = MapError(error);
        return StatusCode(statusCode, problem);
    }

    private static (int StatusCode, object Problem) MapError(Error error)
    {
        return error.Type switch
        {
            ErrorType.NotFound => (
                StatusCodes.Status404NotFound,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    Title = "Not Found",
                    Status = StatusCodes.Status404NotFound,
                    Detail = error.Description
                }),

            ErrorType.Validation => (
                StatusCodes.Status400BadRequest,
                new ValidationProblemDetail
                {
                    Detail = error.Description,
                }),

            ErrorType.Conflict => (
                StatusCodes.Status409Conflict,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                    Title = "Conflict",
                    Status = StatusCodes.Status409Conflict,
                    Detail = error.Description
                }),

            ErrorType.Unauthorized => (
                StatusCodes.Status401Unauthorized,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                    Title = "Unauthorized",
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = error.Description
                }),

            ErrorType.Forbidden => (
                StatusCodes.Status403Forbidden,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    Title = "Forbidden",
                    Status = StatusCodes.Status403Forbidden,
                    Detail = error.Description
                }),

            _ => (
                StatusCodes.Status400BadRequest,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "Bad Request",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = error.Description
                })
        };
    }
}
