using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.ProblemDetails;
using FluxCommerce.BuildingBlocks.Web.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.BuildingBlocks.Web.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    private readonly ILocalizer _localizer;
    private readonly IErrorCodeLocalizer _errorCodeLocalizer;

    protected ApiControllerBase(ILocalizer localizer, IErrorCodeLocalizer errorCodeLocalizer)
    {
        _localizer = localizer;
        _errorCodeLocalizer = errorCodeLocalizer;
    }

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

    /// <summary>
    /// Maps a domain/application <see cref="Error"/> to its ProblemDetails response without
    /// going through a <see cref="Result"/> wrapper.
    /// </summary>
    protected IActionResult FromError(Error error) => ErrorResult(error);

    protected IActionResult Invalid(Dictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetail
        {
            Title = _localizer["Errors.Title.ValidationFailed"],
            Detail = _localizer["Errors.RequestValidation"],
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

    private (int StatusCode, object Problem) MapError(Error error)
    {
        return error.Type switch
        {
            ErrorType.NotFound => (
                StatusCodes.Status404NotFound,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    Title = _localizer["Errors.Title.NotFound"],
                    Status = StatusCodes.Status404NotFound,
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description)
                }),

            ErrorType.Validation => (
                StatusCodes.Status400BadRequest,
                new ValidationProblemDetail
                {
                    Title = _localizer["Errors.Title.ValidationFailed"],
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description),
                }),

            ErrorType.Conflict => (
                StatusCodes.Status409Conflict,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                    Title = _localizer["Errors.Title.Conflict"],
                    Status = StatusCodes.Status409Conflict,
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description)
                }),

            ErrorType.Unauthorized => (
                StatusCodes.Status401Unauthorized,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                    Title = _localizer["Errors.Title.Unauthorized"],
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description)
                }),

            ErrorType.Forbidden => (
                StatusCodes.Status403Forbidden,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    Title = _localizer["Errors.Title.Forbidden"],
                    Status = StatusCodes.Status403Forbidden,
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description)
                }),

            _ => (
                StatusCodes.Status400BadRequest,
                new ProblemDetail
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = _localizer["Errors.Title.BadRequest"],
                    Status = StatusCodes.Status400BadRequest,
                    Detail = _errorCodeLocalizer.Localize(error.Code, error.Description)
                })
        };
    }
}