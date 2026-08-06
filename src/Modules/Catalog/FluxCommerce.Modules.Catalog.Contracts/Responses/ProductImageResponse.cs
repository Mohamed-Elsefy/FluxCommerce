namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

public sealed record ProductImageResponse(
    Guid Id,
    Guid ProductId,
    string ImageUrl,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary,
    Dictionary<string, string>? Metadata,
    DateTime CreatedAtUtc);
