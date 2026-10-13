using FluxCommerce.Modules.Cart.Contracts.Enums;

namespace FluxCommerce.Modules.Cart.Contracts.ReadModels;

/// <summary>
/// Outcome of a best-effort Cart consumption. Retryable conditions (concurrency) and non-retryable
/// conditions are kept distinct so the Orders Application could retry without duplicating work.
/// </summary>
public sealed record CartCheckoutConsumptionResult(CartCheckoutConsumptionStatus Status);
