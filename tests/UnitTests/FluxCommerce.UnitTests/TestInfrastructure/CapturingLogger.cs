using Microsoft.Extensions.Logging;

namespace FluxCommerce.UnitTests.TestInfrastructure;

/// <summary>
/// A lightweight <see cref="ILogger{T}"/> that records structured entries so tests can assert on the
/// structured logging contract — the identifiers (MessageId, EventId, EventType, CorrelationId) that
/// let an operator correlate a dispatch attempt back to a business operation, and the guarantee that
/// serialized business payloads are never logged.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
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

    private readonly List<Logged> _entries = [];

    public IReadOnlyList<Logged> Entries => _entries;

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
        _entries.Add(new Logged(
            logLevel,
            eventId,
            properties?.ToList() ?? [],
            exception,
            formatter(state, exception)));
    }

    /// <summary>
    /// Returns the single entry logged at <paramref name="level"/>, or null if none was logged.
    /// </summary>
    public Logged? Single(LogLevel level) =>
        _entries.SingleOrDefault(entry => entry.Level == level);
}
