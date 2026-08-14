using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.Modules.Orders.Infrastructure.Outbox;

/// <summary>
/// Phase 6.3 save interceptor that appends <see cref="FluxCommerce.BuildingBlocks.Outbox.Messages.OutboxMessage"/>
/// records for the selected Orders domain events before <c>SaveChangesAsync</c> runs. Because the
/// records are added as tracked entities inside <c>SavingChangesAsync</c> — the same save that writes
/// the business state — Business State + Outbox commit or roll back as a single transaction. This
/// deliberately does not write from <c>SavedChangesAsync</c> (post-commit) or a separate transaction.
/// The interceptor is stateless, so it is safe to register as a singleton.
/// </summary>
public sealed class OrdersOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly OrdersOutboxMessageFactory _outboxMessageFactory;

    public OrdersOutboxSaveChangesInterceptor(OrdersOutboxMessageFactory outboxMessageFactory)
    {
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is OrdersDbContext context)
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

    private static List<IDomainEvent> CollectDomainEvents(OrdersDbContext context) =>
        context.ChangeTracker
            .Entries()
            .Where(entry => entry.Entity is IAggregateRoot)
            .SelectMany(entry => ((IAggregateRoot)entry.Entity).GetDomainEvents())
            .ToList();
}