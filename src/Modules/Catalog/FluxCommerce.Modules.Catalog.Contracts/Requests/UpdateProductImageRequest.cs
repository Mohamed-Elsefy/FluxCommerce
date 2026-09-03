namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record UpdateProductImageRequest(
    string? ImageUrl,
    string? AltText,
    int DisplayOrder,
    Dictionary<string, string>? Metadata);
