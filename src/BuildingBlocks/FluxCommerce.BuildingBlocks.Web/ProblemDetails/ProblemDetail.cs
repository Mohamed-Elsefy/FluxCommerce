namespace FluxCommerce.BuildingBlocks.Web.ProblemDetails;

public sealed class ProblemDetail
{
    public string Type { get; set; } = "about:blank";
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? Instance { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, object?> Extensions { get; set; } = [];
}
