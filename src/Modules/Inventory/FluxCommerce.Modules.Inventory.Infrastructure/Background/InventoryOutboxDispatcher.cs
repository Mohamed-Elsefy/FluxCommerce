using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Dispatch;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Background;

/// <summary>
/// Phase 6.7 background processor that dispatches the Inventory outbox to the message broker. Each
/// cycle recovers stale InProgress claims, atomically claims a bounded batch, deserializes each
/// payload to its integration contract, publishes it through <see cref="IRabbitMQPublisher"/>, and
/// marks it published. A failed publication never loses the message: it is returned to Pending (with
/// the retry count and error recorded) so a later cycle re-claims it. Shutdown cancels the current
/// publish and leaves the claim InProgress for stale-claim recovery.
/// </summary>
public sealed class InventoryOutboxDispatcher : BackgroundService, IOutboxDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxDispatchOptions> _options;
    private readonly ILogger<InventoryOutboxDispatcher> _logger;

    public InventoryOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatchOptions> options,
        ILogger<InventoryOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Transient failures must not stop the dispatcher; the next cycle retries.
                _logger.LogError(exception, "Outbox dispatch cycle failed and will be retried.");
            }

            try
            {
                await Task.Delay(_options.Value.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one dispatch cycle inside a fresh scope: recovers stale claims and dispatches a single
    /// batch. The hosted-service loop calls it on each tick; tests call it directly.
    /// </summary>
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<InventoryOutboxStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRabbitMQPublisher>();

        await DispatchCoreAsync(store, publisher, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches one batch through explicitly supplied store and publisher. Internal so unit tests
    /// can drive the exact claim → publish → mark flow with mocks.
    /// </summary>
    internal async Task DispatchCoreAsync(
        IOutboxStore store,
        IRabbitMQPublisher publisher,
        CancellationToken cancellationToken)
    {
        var recovered = await store
            .RecoverExpiredClaimsAsync(_options.Value.ClaimTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (recovered > 0)
            _logger.LogInformation("Recovered {RecoveredCount} stale outbox claim(s).", recovered);

        var claimed = await store
            .ClaimPendingAsync(_options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in claimed)
        {
            await DispatchMessageAsync(message, store, publisher, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchMessageAsync(
        OutboxMessage message,
        IOutboxStore store,
        IRabbitMQPublisher publisher,
        CancellationToken cancellationToken)
    {
        try
        {
            var integrationEvent = Deserialize(message);

            if (integrationEvent is null)
            {
                await store
                    .MarkRetryableFailureAsync(
                        message.Id,
                        $"Unsupported outbox event type '{message.Type}'.",
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var published = await publisher
                .PublishAsync(message.Type, integrationEvent, cancellationToken)
                .ConfigureAwait(false);

            if (!published)
            {
                await store
                    .MarkRetryableFailureAsync(
                        message.Id,
                        "The message broker did not confirm the publication.",
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await store.MarkPublishedAsync(message.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress: leave the claim InProgress so stale-claim recovery completes the
            // message later; a cancelled publish is never reported as a retryable failure.
            throw;
        }
        catch (Exception exception)
        {
            await store
                .MarkRetryableFailureAsync(message.Id, exception.Message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IIntegrationEvent? Deserialize(OutboxMessage message) =>
        message.Type switch
        {
            InventoryReservationExpiredIntegrationEvent.EventType =>
                JsonSerializer.Deserialize<InventoryReservationExpiredIntegrationEvent>(message.Payload),
            _ => null
        };
}