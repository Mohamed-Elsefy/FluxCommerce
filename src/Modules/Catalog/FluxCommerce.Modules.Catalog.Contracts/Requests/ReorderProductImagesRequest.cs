namespace FluxCommerce.Modules.Catalog.Contracts.Requests;

public sealed record ReorderProductImagesRequest(
    IReadOnlyCollection<Guid>? OrderedImageIds);
