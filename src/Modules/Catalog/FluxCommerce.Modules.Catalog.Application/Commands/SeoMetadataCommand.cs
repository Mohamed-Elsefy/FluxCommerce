namespace FluxCommerce.Modules.Catalog.Application.Commands;

public sealed record SeoMetadataCommand(
    string? Title,
    string? Description,
    IReadOnlyList<string>? Keywords);
