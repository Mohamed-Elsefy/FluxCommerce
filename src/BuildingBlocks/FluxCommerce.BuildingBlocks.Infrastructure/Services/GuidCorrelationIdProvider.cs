using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Services;

/// <summary>
/// Fallback <see cref="ICorrelationIdProvider"/> that generates a fresh id per call. Modules register
/// this default so their persistence stack is self-contained; the API host overrides it with the
/// web-backed provider (last registration wins) to propagate the HTTP <c>X-Correlation-ID</c>.
/// </summary>
public sealed class GuidCorrelationIdProvider : ICorrelationIdProvider
{
    public Guid GetCorrelationId() => Guid.NewGuid();
}