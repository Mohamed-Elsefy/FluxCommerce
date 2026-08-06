namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record AddProductImageRequest(
    string? ImageUrl,
    string? AltText,
    bool IsPrimary,
    int DisplayOrder,
    Dictionary<string, string>? Metadata);
