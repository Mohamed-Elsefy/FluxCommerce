using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Web.Responses;

namespace FluxCommerce.BuildingBlocks.Web.Extensions;

public static class ErrorExtensions
{
    public static ErrorResponse ToErrorResponse(this Error error) =>
        new(error.Code, error.Description);
}
