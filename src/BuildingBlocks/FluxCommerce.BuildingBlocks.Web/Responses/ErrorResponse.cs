namespace FluxCommerce.BuildingBlocks.Web.Responses;

public sealed class ErrorResponse
{
    public string Code { get; }
    public string Description { get; }

    public ErrorResponse(string code, string description)
    {
        Code = code;
        Description = description;
    }
}
