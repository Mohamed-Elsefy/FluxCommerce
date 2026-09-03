using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.Common.FaultInjection;

/// <summary>
/// Phase 12.1 RabbitMQ fault simulator for reliability testing. Wraps the real
/// <see cref="IRabbitMQPublisher"/> and injects deterministic publication failures so Outbox
/// dispatcher recovery, stale-claim, retry, and duplicate-tolerance can be verified under
/// broker outages without touching production registrations. Strictly test-scoped.
/// </summary>
/// <remarks>
/// Supports three failure modes:
/// <list type="bullet">
/// <item><c>ReturnFalse</c> — the publish returns <c>false</c> (broker NACK) without throwing,
/// exercising the dispatcher's retry/failure path.</item>
/// <item><c>ThrowPublishException</c> — the publish throws a broker publish exception, exercising
/// exception handling in the dispatch loop.</item>
/// <item><c>ThrowConnectionException</c> — the publish throws a connection-level exception,
/// exercising connection outage and reconnect behavior.</item>
/// </list>
/// Failures are one-shot (N times then auto-clear) and resettable. Disabled by default; arm
/// explicitly per test and call <see cref="Reset"/> in teardown.
/// </remarks>
public sealed class BrokerFaultSimulator : IRabbitMQPublisher
{
    private readonly IRabbitMQPublisher _inner;
    private readonly ILogger<BrokerFaultSimulator> _logger;
    private readonly object _lock = new();

    private BrokerFaultMode _mode = BrokerFaultMode.None;
    private int _remaining;

    public BrokerFaultSimulator(IRabbitMQPublisher inner, ILogger<BrokerFaultSimulator> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ExchangeName => (_inner as RabbitMQPublisher)?.ExchangeName ?? "fault-injection";

    /// <summary>Arms a fault for the next <paramref name="times"/> publish calls.</summary>
    public void InjectFault(BrokerFaultMode mode, int times = 1)
    {
        if (times < 1)
            throw new ArgumentOutOfRangeException(nameof(times), "Fault repetition must be at least 1.");
        if (mode == BrokerFaultMode.None)
            throw new ArgumentOutOfRangeException(nameof(mode), "Use Reset() to disarm, not InjectFault(None).");

        lock (_lock)
        {
            _mode = mode;
            _remaining = times;
        }
    }

    /// <summary>Arms a connection outage for the next <paramref name="times"/> publishes.</summary>
    public void SimulateConnectionOutage(int times = 1) => InjectFault(BrokerFaultMode.ThrowConnectionException, times);

    /// <summary>Arms a broker NACK (publish returns false) for the next <paramref name="times"/> publishes.</summary>
    public void SimulatePublishRejection(int times = 1) => InjectFault(BrokerFaultMode.ReturnFalse, times);

    /// <summary>Arms a publish exception for the next <paramref name="times"/> publishes.</summary>
    public void SimulatePublishException(int times = 1) => InjectFault(BrokerFaultMode.ThrowPublishException, times);

    /// <summary>Clears any armed fault. Safe to call even when no fault is active.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _mode = BrokerFaultMode.None;
            _remaining = 0;
        }
    }

    public bool HasPendingFault
    {
        get
        {
            lock (_lock)
                return _remaining > 0 && _mode != BrokerFaultMode.None;
        }
    }

    public async Task<bool> PublishAsync(string eventType, IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var fault = ConsumeFault();
        if (fault != BrokerFaultMode.None)
        {
            _logger.LogWarning(
                "[FaultInjection:Broker] Simulating {FaultMode} for {EventType} event {EventId} (remaining {Remaining}).",
                fault,
                eventType,
                @event.EventId,
                _remaining);

            return fault switch
            {
                BrokerFaultMode.ReturnFalse => false,
                BrokerFaultMode.ThrowPublishException => throw new InvalidOperationException(
                    $"[FaultInjection:Broker] Simulated publish exception for {eventType} event {@event.EventId}."),
                BrokerFaultMode.ThrowConnectionException => throw new InvalidOperationException(
                    $"[FaultInjection:Broker] Simulated RabbitMQ connection outage for {eventType} event {@event.EventId}."),
                _ => false
            };
        }

        return await _inner.PublishAsync(eventType, @event, cancellationToken).ConfigureAwait(false);
    }

    private BrokerFaultMode ConsumeFault()
    {
        lock (_lock)
        {
            if (_remaining <= 0 || _mode == BrokerFaultMode.None)
                return BrokerFaultMode.None;

            var mode = _mode;
            _remaining--;
            if (_remaining == 0)
                _mode = BrokerFaultMode.None;

            return mode;
        }
    }
}

/// <summary>
/// Deterministic fault modes supported by <see cref="BrokerFaultSimulator"/>.
/// </summary>
public enum BrokerFaultMode
{
    None = 0,
    ReturnFalse = 1,
    ThrowPublishException = 2,
    ThrowConnectionException = 3
}

/// <summary>
/// Factory helper for wiring <see cref="BrokerFaultSimulator"/> in test service collections.
/// </summary>
public static class BrokerFaultSimulatorExtensions
{
    /// <summary>
    /// Decorates the registered <see cref="IRabbitMQPublisher"/> with <see cref="BrokerFaultSimulator"/>
    /// so tests can inject broker outages without replacing the publisher. The simulator delegates to
    /// the original publisher when no fault is armed. Call only in tests.
    /// </summary>
    public static IServiceCollection AddBrokerFaultSimulator(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRabbitMQPublisher));
        if (descriptor is null)
            throw new InvalidOperationException("No IRabbitMQPublisher registration found; cannot add BrokerFaultSimulator.");

        // Capture factory for the inner publisher
        var innerFactory = descriptor.ImplementationFactory;
        var innerType = descriptor.ImplementationType;
        var innerInstance = descriptor.ImplementationInstance;

        services.Remove(descriptor);

        services.AddSingleton<BrokerFaultSimulator>(provider =>
        {
            IRabbitMQPublisher inner;
            if (innerFactory is not null)
                inner = (IRabbitMQPublisher)innerFactory(provider);
            else if (innerInstance is not null)
                inner = (IRabbitMQPublisher)innerInstance;
            else if (innerType is not null)
                inner = (IRabbitMQPublisher)ActivatorUtilities.CreateInstance(provider, innerType);
            else
                throw new InvalidOperationException("Unable to resolve inner IRabbitMQPublisher for BrokerFaultSimulator.");

            var logger = provider.GetRequiredService<ILogger<BrokerFaultSimulator>>();
            return new BrokerFaultSimulator(inner, logger);
        });

        services.AddSingleton<IRabbitMQPublisher>(provider => provider.GetRequiredService<BrokerFaultSimulator>());

        return services;
    }
}
