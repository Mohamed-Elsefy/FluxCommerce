namespace FluxCommerce.BuildingBlocks.Web.Responses;

public sealed class ValidationProblemDetail
{
    public string Type { get; set; } = "https://tools.ietf.org/html/rfc7231#section-6.5.1";
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; } = 400;
    public string Detail { get; set; } = string.Empty;
    public string? Instance { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = [];
}
