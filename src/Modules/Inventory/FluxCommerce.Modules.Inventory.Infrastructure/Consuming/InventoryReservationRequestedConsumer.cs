using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Consuming;

/// <summary>
/// Phase 8.4/8.5: the Inventory reservation-request consumer. It is the transport-agnostic adapter that
/// connects the Orders saga to the Inventory reservation capability through messaging: it maps each
/// <see cref="InventoryReservationRequestedIntegrationEvent"/> (issued by the saga, one per order line
/// item) to the <see cref="ReserveInventoryCommand"/> application use case and delegates the business
/// operation to <see cref="ICommandHandler{TCommand,TResult}"/> (Application), which executes the atomic
/// conditional reservation through the Inventory domain. The consumer never touches RabbitMQ or
/// messaging transport types directly; its stable <see cref="HandlerName"/> scopes the idempotency
/// identity (<c>EventId + HandlerName</c>) enforced by the consumer infrastructure, so the reservation
/// state commits atomically with the Inventory <c>ProcessedMessage</c> record inside the module
/// idempotent-processing boundary.
///
/// <para>
/// Phase 8.5: every reservation request now receives a result event back through the Inventory outbox —
/// <c>inventory.reserved</c> on success (carrying the created reservation snapshot) or
/// <c>inventory.reservation_failed</c> on a business failure (carrying the stable error code). The
/// result outbox record is staged inside the same idempotent-processing unit of work, so it commits
/// atomically with the reservation and the <c>ProcessedMessage</c> record, and it is published by the
/// existing Inventory outbox dispatcher. The produced event preserves the workflow correlation and
/// causally links back to the consumed command (Phase 7.8). A business failure is a valid, acknowledged
/// outcome — never a retry — so the delivery is only retried/dead-lettered for technical exceptions.
/// </para>
/// </summary>
public sealed class InventoryReservationRequestedConsumer : IIntegrationEventHandler<InventoryReservationRequestedIntegrationEvent>
{
    public const string StableHandlerName = "Inventory.ReserveInventory";

    private readonly ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>> _reservationHandler;
    private readonly InventoryDbContext _context;
    private readonly IntegrationEventOutboxMessageFactory _outboxMessageFactory;
    private readonly IDateTimeProvider _dateTimeProvider;

    public InventoryReservationRequestedConsumer(
        ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>> reservationHandler,
        InventoryDbContext context,
        IntegrationEventOutboxMessageFactory outboxMessageFactory,
        IDateTimeProvider dateTimeProvider)
    {
        _reservationHandler = reservationHandler ?? throw new ArgumentNullException(nameof(reservationHandler));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        InventoryReservationRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        return HandleAsyncCore(@event, envelope, cancellationToken);
    }

    private async Task HandleAsyncCore(
        InventoryReservationRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var result = await _reservationHandler
            .HandleAsync(
                new ReserveInventoryCommand(@event.OrderId, @event.Sku, @event.Quantity, @event.ExpiresAt),
                cancellationToken)
            .ConfigureAwait(false);

        var occurredAtUtc = _dateTimeProvider.UtcNow;

        if (result.IsFailure)
        {
            // Phase 8.5 failure semantics: a reservation that could not be created is a valid business
            // outcome. The failure result event is staged (atomic with the ProcessedMessage record) and
            // the delivery is acknowledged — the Orders side reacts to inventory.reservation_failed.
            var failed = new InventoryReservationFailedIntegrationEvent(
                @event.OrderId,
                @event.Sku,
                @event.Quantity,
                result.Error.Code)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = envelope.ProducedEventCorrelationId,
                CausationId = envelope.ProducedEventCausationId,
                OccurredAtUtc = occurredAtUtc
            };

            _context.OutboxMessages.Add(
                _outboxMessageFactory.Create(InventoryReservationFailedIntegrationEvent.EventType, failed));
            return;
        }

        var reservation = result.Value;

        var reserved = new InventoryReservedIntegrationEvent(
            reservation.ReservationId,
            reservation.OrderId,
            reservation.Sku,
            reservation.Quantity,
            reservation.ExpiresAt,
            MapStatus(reservation.Status),
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc,
            reservation.Version)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = envelope.ProducedEventCorrelationId,
            CausationId = envelope.ProducedEventCausationId,
            OccurredAtUtc = occurredAtUtc
        };

        _context.OutboxMessages.Add(
            _outboxMessageFactory.Create(InventoryReservedIntegrationEvent.EventType, reserved));
    }

    private static InventoryReservationStatus MapStatus(DomainReservationStatus status) => status switch
    {
        DomainReservationStatus.Pending => InventoryReservationStatus.Pending,
        DomainReservationStatus.Committed => InventoryReservationStatus.Committed,
        DomainReservationStatus.Released => InventoryReservationStatus.Released,
        DomainReservationStatus.Expired => InventoryReservationStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown reservation status.")
    };
}