using FluxCommerce.BuildingBlocks.Idempotency.Messages;

namespace FluxCommerce.BuildingBlocks.Idempotency.Stores;

public interface IIdempotencyStore
{
    Task<ProcessedMessage?> GetAsync(string key, string? handler = null, CancellationToken cancellationToken = default);

    Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default);
}
