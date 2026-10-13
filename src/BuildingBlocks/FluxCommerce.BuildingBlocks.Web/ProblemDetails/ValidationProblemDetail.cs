namespace FluxCommerce.BuildingBlocks.Web.ProblemDetails;

/// <summary>
/// Validation failure shape (HTTP 400): a <see cref="ProblemDetail"/> plus the
/// per-field <see cref="Errors"/> map. Co-located with <see cref="ProblemDetail"/>
/// so both envelopes evolve together; the wire shape is unchanged.
/// </summary>
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
