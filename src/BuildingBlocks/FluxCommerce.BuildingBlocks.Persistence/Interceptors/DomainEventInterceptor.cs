using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.BuildingBlocks.Domain.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.BuildingBlocks.Persistence.Interceptors;

public sealed class DomainEventInterceptor : SaveChangesInterceptor
{
    private readonly List<IDomainEvent> _capturedEvents = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _capturedEvents.Clear();

        var domainEvents = eventData.Context?.ChangeTracker
            .Entries()
            .Where(e => e.Entity is IAggregateRoot)
            .SelectMany(e =>
            {
                var aggregate = (IAggregateRoot)e.Entity;
                return aggregate.GetDomainEvents();
            })
            .ToList();

        if (domainEvents?.Count > 0)
        {
            _capturedEvents.AddRange(domainEvents);
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_capturedEvents.Count > 0)
        {
            await OnTransactionCompleted(cancellationToken);
            ClearCapturedEvents(eventData);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _capturedEvents.Clear();
        base.SaveChangesFailed(eventData);
    }

    private Task OnTransactionCompleted(CancellationToken cancellationToken)
    {
        // Hook: captured domain events will be inserted into the outbox here
        return Task.CompletedTask;
    }

    private void ClearCapturedEvents(DbContextEventData eventData)
    {
        var aggregates = eventData.Context?.ChangeTracker
            .Entries()
            .Where(e => e.Entity is IAggregateRoot)
            .Select(e => (IAggregateRoot)e.Entity)
            .ToList();

        if (aggregates?.Count > 0)
        {
            foreach (var aggregate in aggregates)
            {
                aggregate.ClearDomainEvents();
            }
        }

        _capturedEvents.Clear();
    }
}
