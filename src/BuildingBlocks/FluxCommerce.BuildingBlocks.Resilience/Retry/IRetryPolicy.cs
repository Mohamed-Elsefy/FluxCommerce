namespace FluxCommerce.BuildingBlocks.Resilience.Retry;

public interface IRetryPolicy
{
    Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default);

    Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
