namespace FluxCommerce.BuildingBlocks.Web.Responses;

public class ApiResponse
{
    public bool IsSuccess { get; }
    public string? Message { get; }

    protected ApiResponse(bool isSuccess, string? message = null)
    {
        IsSuccess = isSuccess;
        Message = message;
    }

    public static ApiResponse Ok(string? message = null) =>
        new(true, message ?? "Operation completed successfully.");

    public static ApiResponse<T> Ok<T>(T data, string? message = null) =>
        ApiResponse<T>.Create(data, message);

    public static ApiResponse Fail(string message) =>
        new(false, message);
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; }

    private ApiResponse(bool isSuccess, T? data, string? message)
        : base(isSuccess, message)
    {
        Data = data;
    }

    public static ApiResponse<T> Create(T data, string? message = null) =>
        new(true, data, message ?? "Operation completed successfully.");

    public static ApiResponse<T> Failure(string message) =>
        new(false, default, message);
}
