using FluxCommerce.BuildingBlocks.Idempotency.Messages;

namespace FluxCommerce.BuildingBlocks.Idempotency.Detection;

public interface IDuplicateDetector
{
    Task<IdempotencyResult> TryClaimAsync(ProcessedMessage message, CancellationToken cancellationToken = default);

    Task<bool> IsProcessedAsync(string key, string? handler = null, CancellationToken cancellationToken = default);
}
