using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluxCommerce.IntegrationTests.Common.FaultInjection;

/// <summary>
/// Phase 12.1 EF Core fault-injection interceptor for reliability testing.
/// Strictly test-scoped — never registered in production. Simulates deterministic database failures
/// at distinct transaction boundaries so the Outbox atomicity, retry, and compensation invariants
/// can be verified under failure. Disabled by default; faults are injected explicitly per test and
/// reset afterwards.
/// </summary>
/// <remarks>
/// Supports two interception surfaces:
/// <list type="bullet">
/// <item><c>ISaveChangesInterceptor</c> — before database commit (SavingChanges). Throws before EF issues
/// the transaction commit, so Business State + Outbox must roll back atomically.</item>
/// <item><c>DbCommandInterceptor</c> — during SQL command execution (Reader/NonQuery/Scalar). Throws while
/// a command is in-flight to simulate timeouts, connection drops, and constraint violations at the
/// transport layer.</item>
/// </list>
/// Faults are one-shot by default (applied N times then auto-cleared) to keep failures deterministic
/// and to guarantee a clean retry path. Use <see cref="Reset"/> between tests.
/// </remarks>
public sealed class DbFaultInterceptor : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly object _lock = new();

    private DbFaultKind _saveChangesFault = DbFaultKind.None;
    private int _saveChangesRemaining;

    private DbFaultKind _commandFault = DbFaultKind.None;
    private int _commandRemaining;

    // ------------------------------------------------------------------ configuration

    /// <summary>
    /// Injects a fault that will be thrown during the next <paramref name="times"/> SaveChanges calls
    /// (the SavingChanges phase, before commit). Pass <see cref="DbFaultKind.None"/> to disable.
    /// </summary>
    public void InjectSaveChangesFault(DbFaultKind kind, int times = 1)
    {
        if (times < 1)
            throw new ArgumentOutOfRangeException(nameof(times), "Fault repetition must be at least 1.");

        lock (_lock)
        {
            _saveChangesFault = kind;
            _saveChangesRemaining = kind == DbFaultKind.None ? 0 : times;
        }
    }

    /// <summary>
    /// Injects a fault that will be thrown during the next <paramref name="times"/> DB command
    /// executions (Reader/NonQuery/Scalar). Pass <see cref="DbFaultKind.None"/> to disable.
    /// </summary>
    public void InjectCommandFault(DbFaultKind kind, int times = 1)
    {
        if (times < 1)
            throw new ArgumentOutOfRangeException(nameof(times), "Fault repetition must be at least 1.");

        lock (_lock)
        {
            _commandFault = kind;
            _commandRemaining = kind == DbFaultKind.None ? 0 : times;
        }
    }

    /// <summary>
    /// Clears all pending faults. Safe to call between tests even when no fault is active.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _saveChangesFault = DbFaultKind.None;
            _saveChangesRemaining = 0;
            _commandFault = DbFaultKind.None;
            _commandRemaining = 0;
        }
    }

    /// <summary>Whether any fault is currently armed (save-changes or command).</summary>
    public bool HasPendingFault
    {
        get
        {
            lock (_lock)
                return _saveChangesRemaining > 0 || _commandRemaining > 0;
        }
    }

    // ---------------------------------------------------------------- ISaveChangesInterceptor

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var fault = ConsumeSaveChangesFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, "SavingChanges");

        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var fault = ConsumeSaveChangesFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, "SavingChanges");

        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(result);

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result) => result;

    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
    }

    public Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    // -------------------------------------------------------------- DbCommandInterceptor

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.Reader [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.Reader [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.NonQuery [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.NonQuery [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.Scalar [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        var fault = ConsumeCommandFault();
        if (fault != DbFaultKind.None)
            throw CreateException(fault, $"DbCommand.Scalar [{command.CommandText[..Math.Min(80, command.CommandText.Length)]}]");

        return base.ScalarExecuting(command, eventData, result);
    }

    // ---------------------------------------------------------------- internals

    private DbFaultKind ConsumeSaveChangesFault()
    {
        lock (_lock)
        {
            if (_saveChangesRemaining <= 0 || _saveChangesFault == DbFaultKind.None)
                return DbFaultKind.None;

            var kind = _saveChangesFault;
            _saveChangesRemaining--;

            if (_saveChangesRemaining == 0)
                _saveChangesFault = DbFaultKind.None;

            return kind;
        }
    }

    private DbFaultKind ConsumeCommandFault()
    {
        lock (_lock)
        {
            if (_commandRemaining <= 0 || _commandFault == DbFaultKind.None)
                return DbFaultKind.None;

            var kind = _commandFault;
            _commandRemaining--;

            if (_commandRemaining == 0)
                _commandFault = DbFaultKind.None;

            return kind;
        }
    }

    private static Exception CreateException(DbFaultKind kind, string phase) => kind switch
    {
        DbFaultKind.Timeout => new TimeoutException(
            $"[FaultInjection:{phase}] Simulated database timeout."),

        DbFaultKind.ConnectionDrop => new InvalidOperationException(
            $"[FaultInjection:{phase}] Simulated sudden connection drop."),

        DbFaultKind.PrimaryKeyViolation => new DbUpdateException(
            $"[FaultInjection:{phase}] Simulated primary key violation.",
            new InvalidOperationException("Violation of PRIMARY KEY constraint. Cannot insert duplicate key.")),

        DbFaultKind.Deadlock => new DbUpdateException(
            $"[FaultInjection:{phase}] Simulated deadlock victim.",
            new InvalidOperationException("Transaction was deadlocked on lock resources with another process and has been chosen as the deadlock victim.")),

        DbFaultKind.TransientFailure => new InvalidOperationException(
            $"[FaultInjection:{phase}] Simulated transient failure (retryable)."),

        _ => new InvalidOperationException($"[FaultInjection:{phase}] Simulated fault {kind}.")
    };
}

/// <summary>
/// Deterministic fault kinds supported by <see cref="DbFaultInterceptor"/>.
/// </summary>
public enum DbFaultKind
{
    None = 0,
    Timeout = 1,
    ConnectionDrop = 2,
    PrimaryKeyViolation = 3,
    Deadlock = 4,
    TransientFailure = 5
}
