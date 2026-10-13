using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Extensions;
using Microsoft.AspNetCore.Http;

namespace FluxCommerce.BuildingBlocks.Web.Services;

/// <summary>
/// Web-backed <see cref="ICorrelationIdProvider"/>. Inside an HTTP request it returns the correlation
/// id assigned by the correlation middleware (from the <c>X-Correlation-ID</c> header or a
/// freshly generated id); outside a request (e.g. background services) it generates a fresh id. The
/// provider is stateless, so it is registered as a singleton.
/// </summary>
public sealed class WebCorrelationIdProvider : ICorrelationIdProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WebCorrelationIdProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public Guid GetCorrelationId()
    {
        var correlationId = _httpContextAccessor.HttpContext?.TryGetCorrelationId();

        return Guid.TryParse(correlationId, out var parsed) ? parsed : Guid.NewGuid();
    }
}
