namespace FluxCommerce.IntegrationTests.TestInfrastructure;

public sealed record ApiResponseDto<T>(bool IsSuccess, string? Message, T? Data);

public sealed record ProblemDetailsDto(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? Instance,
    string? TraceId);
