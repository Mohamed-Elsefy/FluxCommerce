using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Consuming;

/// <summary>
/// Phase 8.4: the Inventory reservation-request consumer. It is the transport-agnostic adapter that
/// connects the Orders saga to the Inventory reservation capability through messaging: it maps each
/// <see cref="InventoryReservationRequestedIntegrationEvent"/> (issued by the saga, one per order line
/// item) to the <see cref="ReserveInventoryCommand"/> application use case and delegates the entire
/// business operation to <see cref="ICommandHandler{TCommand,TResult}"/> (Application), which executes
/// the atomic conditional reservation through the Inventory domain. The consumer never touches
/// RabbitMQ, messaging transport types, or the DbContext directly; its stable <see cref="HandlerName"/>
/// scopes the idempotency identity (<c>EventId + HandlerName</c>) enforced by the consumer
/// infrastructure, so the reservation state commits atomically with the Inventory
/// <c>ProcessedMessage</c> record inside the module idempotent-processing boundary.
///
/// <para>
/// The reserved/failed outcome is intentionally not published here: the result events
/// (<c>inventory.reserved</c> / <c>inventory.reservation_failed</c>) are introduced by Phase 8.5.
/// Until then a failed reservation is surfaced as an exception so the delivery is never acknowledged
/// as a success without a reservation — it is retried and, if the business condition persists,
/// dead-lettered (per the Phase 7.7 consumer semantics, never silently lost).
/// </para>
/// </summary>
public sealed class InventoryReservationRequestedConsumer : IIntegrationEventHandler<InventoryReservationRequestedIntegrationEvent>
{
    public const string StableHandlerName = "Inventory.ReserveInventory";

    private readonly ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>> _reservationHandler;

    public InventoryReservationRequestedConsumer(
        ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>> reservationHandler)
    {
        _reservationHandler = reservationHandler ?? throw new ArgumentNullException(nameof(reservationHandler));
    }

    public string HandlerName => StableHandlerName;

    public async Task HandleAsync(
        InventoryReservationRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        var result = await _reservationHandler
            .HandleAsync(
                new ReserveInventoryCommand(@event.OrderId, @event.Sku, @event.Quantity, @event.ExpiresAt),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            // Phase 8.4 failure semantics: a reservation that could not be created must not be ACKed
            // as a processed success. The exception propagates through the idempotent-processing
            // boundary (rolling back the ProcessedMessage record) and is retried by the consumer
            // retry policy before the delivery dead-letters. Phase 8.5 replaces this throw with the
            // publication of an inventory.reservation_failed result event.
            throw new InvalidOperationException(
                $"Reservation for order '{@event.OrderId}' (sku '{@event.Sku}', quantity {@event.Quantity}) failed: {result.Error.Code}.");
        }
    }
}