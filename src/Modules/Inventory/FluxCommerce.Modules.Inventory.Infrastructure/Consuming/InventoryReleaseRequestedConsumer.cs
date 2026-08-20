using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Errors;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Consuming;

/// <summary>
/// Phase 8.7: the Inventory release-request consumer. It is the transport-agnostic adapter that connects
/// the Orders saga compensation to the Inventory release capability through messaging: it maps each
/// <see cref="InventoryReleaseRequestedIntegrationEvent"/> (issued by the saga when a cancelled order must
/// release its previously reserved inventory) to the <see cref="ReleaseReservationCommand"/> application
/// use case and delegates the business operation to <see cref="ICommandHandler{TCommand,TResult}"/>
/// (Application), which executes the atomic release through the Inventory domain. The consumer never
/// touches RabbitMQ or messaging transport types directly; its stable <see cref="HandlerName"/> scopes
/// the idempotency identity (<c>EventId + HandlerName</c>) enforced by the consumer infrastructure, so
/// the release state commits atomically with the Inventory <c>ProcessedMessage</c> record inside the
/// module idempotent-processing boundary.
///
/// <para>
/// Release idempotency: a repeated release command against an already-terminal reservation
/// (<c>Released</c>, <c>Committed</c> or <c>Expired</c>, or a lost optimistic-concurrency race) fails
/// with <c>Inventory.InvalidStateTransition</c>; the consumer treats that as a benign no-op and
/// acknowledges the delivery without side effects — releasing a terminal reservation never returns
/// reserved quantity twice (the Inventory release use case is itself idempotent). An anomaly (the
/// reservation is missing, or the release fails for any other reason) is a technical failure: the
/// consumer throws, the infrastructure retries the delivery and then dead-letters it, so a release is
/// never silently lost.
/// </para>
///
/// <para>
/// On a successful release the consumer stages <c>inventory.released</c> in the Inventory outbox —
/// inside the same idempotent-processing unit of work, so it commits atomically with the release and
/// the <c>ProcessedMessage</c> record. The produced event preserves the workflow correlation and
/// causally links back to the consumed command (Phase 7.8). Release is fire-and-forget: the Orders saga
/// never consumes the released event, but it is published for observability and future compensation
/// tracking.
/// </para>
/// </summary>
public sealed class InventoryReleaseRequestedConsumer : IIntegrationEventHandler<InventoryReleaseRequestedIntegrationEvent>
{
    public const string StableHandlerName = "Inventory.ReleaseReservation";

    private readonly ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>> _releaseHandler;
    private readonly InventoryDbContext _context;
    private readonly IntegrationEventOutboxMessageFactory _outboxMessageFactory;
    private readonly IDateTimeProvider _dateTimeProvider;

    public InventoryReleaseRequestedConsumer(
        ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>> releaseHandler,
        InventoryDbContext context,
        IntegrationEventOutboxMessageFactory outboxMessageFactory,
        IDateTimeProvider dateTimeProvider)
    {
        _releaseHandler = releaseHandler ?? throw new ArgumentNullException(nameof(releaseHandler));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _outboxMessageFactory = outboxMessageFactory ?? throw new ArgumentNullException(nameof(outboxMessageFactory));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        InventoryReleaseRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        return HandleAsyncCore(@event, envelope, cancellationToken);
    }

    private async Task HandleAsyncCore(
        InventoryReleaseRequestedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var result = await _releaseHandler
            .HandleAsync(new ReleaseReservationCommand(@event.ReservationId), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            // Phase 8.7 idempotency semantics: releasing an already-terminal reservation is a benign
            // no-op (never a retry). Any other failure is an anomaly the consumer cannot resolve — the
            // delivery is retried and dead-lettered so a release is never silently lost.
            if (result.Error.Code == InventoryReservationErrors.InvalidStateTransition.Code)
                return;

            throw new InvalidOperationException(
                $"Inventory release failed for reservation '{@event.ReservationId}' with '{result.Error.Code}': {result.Error.Description}");
        }

        var reservation = result.Value;

        var released = new InventoryReleasedIntegrationEvent(
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
            OccurredAtUtc = _dateTimeProvider.UtcNow
        };

        _context.OutboxMessages.Add(
            _outboxMessageFactory.Create(InventoryReleasedIntegrationEvent.EventType, released));
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