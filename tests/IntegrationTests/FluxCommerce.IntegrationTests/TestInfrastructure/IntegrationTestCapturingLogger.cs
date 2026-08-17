using Microsoft.Extensions.Logging;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// A thread-safe <see cref="ILogger{T}"/> that records structured entries so integration tests can
/// assert on the Phase 7.8 consumer observability contract. The consumer processes deliveries on
/// background threads while the test thread waits for entries, so reads take an immutable snapshot
/// under a lock (unlike the unit-test logger, which is used single-threaded).
/// </summary>
public sealed class IntegrationTestCapturingLogger<T> : ILogger<T>
{
    public sealed record Logged(
        LogLevel Level,
        EventId EventId,
        IReadOnlyList<KeyValuePair<string, object?>> Properties,
        Exception? Exception,
        string Message)
    {
        public object? this[string key] =>
            Properties.FirstOrDefault(pair => pair.Key == key).Value;
    }

    private readonly object _gate = new();
    private readonly List<Logged> _entries = [];

    public IReadOnlyList<Logged> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state as IEnumerable<KeyValuePair<string, object?>>;
        var entry = new Logged(
            logLevel,
            eventId,
            properties?.ToList() ?? [],
            exception,
            formatter(state, exception));

        lock (_gate)
        {
            _entries.Add(entry);
        }
    }
}
