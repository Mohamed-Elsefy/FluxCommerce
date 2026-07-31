using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Web.Responses;

namespace FluxCommerce.BuildingBlocks.Web.Extensions;

public static class ResultExtensions
{
    public static ApiResponse<T> ToApiResponse<T>(this Result<T> result, string? successMessage = null) =>
        result.IsSuccess
            ? ApiResponse<T>.Create(result.Value, successMessage)
            : ApiResponse<T>.Failure(result.Error.Description);

    public static ApiResponse ToApiResponse(this Result result, string? successMessage = null) =>
        result.IsSuccess
            ? ApiResponse.Ok(successMessage)
            : ApiResponse.Fail(result.Error.Description);
}
