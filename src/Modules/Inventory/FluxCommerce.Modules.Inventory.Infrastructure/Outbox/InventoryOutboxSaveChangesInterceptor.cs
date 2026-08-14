using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Outbox;

/// <summary>
/// Phase 6.3 save interceptor that appends <see cref="FluxCommerce.BuildingBlocks.Outbox.Messages.OutboxMessage"/>
/// records for the selected Inventory domain events before <c>SaveChangesAsync</c> runs. Because the
/// records are added as tracked entities inside <c>SavingChangesAsync</c> — the same save that writes
/// the business state (including the explicit transaction in the reservation store) — Business State +
/// Outbox commit or roll back as a single transaction. This deliberately does not write from
/// <c>SavedChangesAsync</c> (post-commit) or a separate transaction. The interceptor is stateless, so
/// it is safe to register as a singleton.
/// </summary>
public sealed class InventoryOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly InventoryOutboxMessageFactory _outboxMessageFactory;

    public InventoryOutboxSaveChangesInterceptor(InventoryOutboxMessageFactory outboxMessageFactory)
    {
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is InventoryDbContext context)
        {
            var domainEvents = CollectDomainEvents(context);

            if (domainEvents.Count > 0)
            {
                foreach (var message in _outboxMessageFactory.Create(domainEvents))
                {
                    context.OutboxMessages.Add(message);
                }
            }
        }

        return ValueTask.FromResult(result);
    }

    private static List<IDomainEvent> CollectDomainEvents(InventoryDbContext context) =>
        context.ChangeTracker
            .Entries()
            .Where(entry => entry.Entity is IAggregateRoot)
            .SelectMany(entry => ((IAggregateRoot)entry.Entity).GetDomainEvents())
            .ToList();
}