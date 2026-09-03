namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record SeoResponse(
    string? Title,
    string? Description,
    IReadOnlyList<string> Keywords);
