using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Domain.ValueObjects;

public sealed class LockoutState : ValueObject
{
    public const int DefaultMaxFailedAccessAttempts = 5;

    public static readonly TimeSpan DefaultLockoutDuration = TimeSpan.FromMinutes(15);

    public int FailedAccessAttemptCount { get; }

    public DateTime? LockoutEndUtc { get; }

    private LockoutState(int failedAccessAttemptCount, DateTime? lockoutEndUtc)
    {
        if (failedAccessAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(failedAccessAttemptCount), "Failed access attempt count must be non-negative.");

        FailedAccessAttemptCount = failedAccessAttemptCount;
        LockoutEndUtc = lockoutEndUtc;
    }

    public static LockoutState Create() => new(0, null);

    public static LockoutState Create(int failedAccessAttemptCount, DateTime? lockoutEndUtc) =>
        new(failedAccessAttemptCount, lockoutEndUtc);

    public bool IsLockedAt(DateTime utcNow) =>
        LockoutEndUtc is { } lockoutEnd && utcNow < lockoutEnd;

    public LockoutState RecordFailedAttempt(DateTime utcNow)
    {
        if (IsLockedAt(utcNow))
            return this;

        var attempts = FailedAccessAttemptCount + 1;

        return attempts >= DefaultMaxFailedAccessAttempts
            ? new LockoutState(0, utcNow.Add(DefaultLockoutDuration))
            : new LockoutState(attempts, null);
    }

    public LockoutState Reset() => new(0, null);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return FailedAccessAttemptCount;
        yield return LockoutEndUtc ?? DateTime.MinValue;
    }
}
