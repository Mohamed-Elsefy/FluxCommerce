using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.ReadModels;

/// <summary>
/// Read-side projection of a Payment for administrative list and detail queries.
/// </summary>
public sealed record PaymentAdminReadModel(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    string? ProviderPaymentIntentId,
    string? Provider,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
