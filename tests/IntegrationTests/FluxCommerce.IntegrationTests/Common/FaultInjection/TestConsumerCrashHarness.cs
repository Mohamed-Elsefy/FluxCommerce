using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.Common.FaultInjection;

/// <summary>
/// Phase 12.1 harness to simulate consumer crashes immediately after the database commit but
/// before the broker acknowledgment (ACK). Under at-least-once delivery the broker will redeliver
/// the same message; the idempotent consumer must then deduplicate it so only one business effect
/// remains. Strictly test-scoped — never registered in production.
/// </summary>
/// <remarks>
/// Two crash points are modeled:
/// <list type="bullet">
/// <item><c>CrashBeforeCommit</c> — the handler throws inside the idempotent transaction. The
/// transaction rolls back, no ProcessedMessage is persisted, and the message stays retryable.</item>
/// <item><c>CrashAfterCommitBeforeAck</c> — the idempotent transaction commits (Business State +
/// ProcessedMessage durable), then a crash occurs before the ACK is sent. On redelivery the
/// consumer detects the duplicate via the (Key, Handler) PK and ACKs without re-executing business logic.</item>
/// </list>
/// The harness uses an <see cref="AsyncLocal{T}"/> slot so each async test flow controls its own
/// crash deterministically without cross-test leaking. Call <see cref="Reset"/> in teardown.
/// </remarks>
public static class TestConsumerCrashHarness
{
    private sealed class CrashState
    {
        public ConsumerCrashMode? Mode;
        public int Remaining;
    }

    private static readonly AsyncLocal<CrashState?> _state = new();

    /// <summary>Arms a crash of the given <paramref name="mode"/> for the next <paramref name="times"/> deliveries.</summary>
    public static void Arm(ConsumerCrashMode mode, int times = 1)
    {
        if (times < 1)
            throw new ArgumentOutOfRangeException(nameof(times), "Crash repetition must be at least 1.");
        if (mode == ConsumerCrashMode.None)
            throw new ArgumentOutOfRangeException(nameof(mode), "Use Reset() to disarm, not Arm(None).");

        _state.Value = new CrashState { Mode = mode, Remaining = times };
    }

    /// <summary>Arms a crash after DB commit but before ACK for the next <paramref name="times"/> deliveries.</summary>
    public static void CrashNextAfterCommit(int times = 1) => Arm(ConsumerCrashMode.AfterCommitBeforeAck, times);

    /// <summary>Arms a crash before DB commit (inside the transaction) for the next <paramref name="times"/> deliveries.</summary>
    public static void CrashNextBeforeCommit(int times = 1) => Arm(ConsumerCrashMode.BeforeCommit, times);

    /// <summary>Clears any armed crash. Safe to call even when no crash is armed.</summary>
    public static void Reset()
    {
        _state.Value = null;
    }

    internal static bool TryConsumeAfterCommitCrash(out ConsumerCrashException? exception)
    {
        var state = _state.Value;
        if (state is null || state.Mode != ConsumerCrashMode.AfterCommitBeforeAck || state.Remaining <= 0)
        {
            exception = null;
            return false;
        }

        state.Remaining--;
        if (state.Remaining == 0)
            state.Mode = null;

        exception = new ConsumerCrashException("Simulated consumer crash after DB commit but before ACK (redelivery expected; idempotency must deduplicate).");
        return true;
    }

    internal static bool ShouldCrashBeforeCommit()
    {
        var state = _state.Value;
        if (state is null || state.Mode != ConsumerCrashMode.BeforeCommit || state.Remaining <= 0)
            return false;

        state.Remaining--;
        if (state.Remaining == 0)
            state.Mode = null;

        return true;
    }
}

/// <summary>
/// Deterministic crash injection modes for <see cref="TestConsumerCrashHarness"/>.
/// </summary>
public enum ConsumerCrashMode
{
    None = 0,
    BeforeCommit = 1,
    AfterCommitBeforeAck = 2
}

/// <summary>
/// Exception thrown by the crash harness. Classified as retryable by the consumer infrastructure
/// so it follows the retry → redelivery path rather than immediate dead-letter.
/// </summary>
public sealed class ConsumerCrashException : Exception
{
    public ConsumerCrashException(string message) : base(message)
    {
    }

    public ConsumerCrashException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

// ---------------------------------------------------------------- decorators

/// <summary>
/// Test-only <see cref="IIdempotentProcessor"/> decorator that injects a crash after the inner
/// processor has committed Business State + ProcessedMessage but before the caller ACKs. The
/// successful commit is preserved (the ProcessedMessage stays durable), so a redelivered message
/// must be detected as a duplicate.
/// </summary>
public sealed class CrashingIdempotentProcessorDecorator : IIdempotentProcessor
{
    private readonly IIdempotentProcessor _inner;

    public CrashingIdempotentProcessorDecorator(IIdempotentProcessor inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<IdempotencyResult> TryProcessAsync(
        ProcessedMessage message,
        Func<Task> businessOperation,
        CancellationToken cancellationToken = default)
    {
        // Before-commit crash: throw inside the business operation so the transaction rolls back
        // and no ProcessedMessage is persisted — the message remains retryable.
        Func<Task> wrappedOperation = async () =>
        {
            if (TestConsumerCrashHarness.ShouldCrashBeforeCommit())
                throw new ConsumerCrashException("Simulated consumer crash before DB commit (transaction must roll back).");

            await businessOperation().ConfigureAwait(false);
        };

        var result = await _inner.TryProcessAsync(message, wrappedOperation, cancellationToken).ConfigureAwait(false);

        // After-commit-before-ACK crash: the inner processor already committed Business State +
        // ProcessedMessage; now crash before the ACK is sent so the broker redelivers and the
        // duplicate must be deduplicated. Duplicates must not re-crash (they already satisfy idempotency).
        if (!result.IsDuplicate && TestConsumerCrashHarness.TryConsumeAfterCommitCrash(out var crash))
            throw crash!;

        return result;
    }
}

/// <summary>
/// Test-only handler decorator that delegates to an inner handler and optionally crashes
/// after the handler's business logic but still inside the idempotent transaction (before-commit).
/// For after-commit crashes use <see cref="CrashingIdempotentProcessorDecorator"/> instead.
/// </summary>
public sealed class CrashingHandlerDecorator<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : class, IIntegrationEvent
{
    private readonly IIntegrationEventHandler<TEvent> _inner;

    public CrashingHandlerDecorator(IIntegrationEventHandler<TEvent> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string HandlerName => _inner.HandlerName;

    public async Task HandleAsync(TEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await _inner.HandleAsync(@event, envelope, cancellationToken).ConfigureAwait(false);

        if (TestConsumerCrashHarness.ShouldCrashBeforeCommit())
            throw new ConsumerCrashException($"Simulated crash inside handler {HandlerName} before commit.");
    }
}

/// <summary>
/// Extension helpers for wiring crash injection in test service collections.
/// </summary>
public static class TestConsumerCrashHarnessExtensions
{
    /// <summary>
    /// Wraps the registered <typeparamref name="TProcessor"/> with <see cref="CrashingIdempotentProcessorDecorator"/>
    /// so after-commit crashes can be injected without touching production registrations. Call only in tests.
    /// </summary>
    public static IServiceCollection DecorateProcessorWithCrashHarness<TProcessor>(this IServiceCollection services)
        where TProcessor : class, IIdempotentProcessor
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TProcessor));
        if (descriptor is null)
            throw new InvalidOperationException($"No registration for {typeof(TProcessor).Name} found; cannot decorate with crash harness.");

        services.Remove(descriptor);

        services.AddScoped<IIdempotentProcessor>(provider =>
        {
            var inner = (IIdempotentProcessor)ActivatorUtilities.CreateInstance(provider, typeof(TProcessor));
            return new CrashingIdempotentProcessorDecorator(inner);
        });

        // Keep concrete resolution working for descriptor-based resolution that uses the concrete type
        services.AddScoped(provider =>
        {
            var inner = (TProcessor)ActivatorUtilities.CreateInstance(provider, typeof(TProcessor));
            return new CrashingIdempotentProcessorDecorator(inner) as IIdempotentProcessor;
        });

        return services;
    }
}
