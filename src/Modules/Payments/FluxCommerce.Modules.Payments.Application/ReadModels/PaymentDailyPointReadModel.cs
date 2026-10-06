using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.ReadModels;

/// <summary>
/// Lightweight daily-series point for a Payment. The succeeded-only revenue filter runs in
/// memory at the consumer, so the projection carries the status and stays a lightweight
/// provider-portable stream (no date-part grouping in the database).
/// </summary>
public sealed record PaymentDailyPointReadModel(
    DateTimeOffset CreatedAtUtc,
    decimal Amount,
    string Currency,
    PaymentStatus Status);
