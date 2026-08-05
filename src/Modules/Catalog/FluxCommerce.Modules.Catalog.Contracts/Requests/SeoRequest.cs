namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record SeoRequest(
    string? Title,
    string? Description,
    string[]? Keywords);
