namespace FluxCommerce.BuildingBlocks.Outbox.Dispatch;

public interface IOutboxDispatcher
{
    Task DispatchPendingAsync(CancellationToken cancellationToken = default);
}
