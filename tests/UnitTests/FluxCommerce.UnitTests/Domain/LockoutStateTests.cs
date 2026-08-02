using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class LockoutStateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_HasNoFailuresAndIsNotLocked()
    {
        var state = LockoutState.Create();

        Assert.Equal(0, state.FailedAccessAttemptCount);
        Assert.Null(state.LockoutEndUtc);
        Assert.False(state.IsLockedAt(UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_IncrementsCountUntilThreshold()
    {
        var state = LockoutState.Create();

        state = state.RecordFailedAttempt(UtcNow);
        state = state.RecordFailedAttempt(UtcNow);
        state = state.RecordFailedAttempt(UtcNow);
        state = state.RecordFailedAttempt(UtcNow);

        Assert.Equal(4, state.FailedAccessAttemptCount);
        Assert.Null(state.LockoutEndUtc);
        Assert.False(state.IsLockedAt(UtcNow));
    }

    [Fact]
    public void RecordFailedAttempt_AtThreshold_LocksAccount()
    {
        var state = LockoutState.Create();

        for (var i = 0; i < LockoutState.DefaultMaxFailedAccessAttempts; i++)
        {
            state = state.RecordFailedAttempt(UtcNow);
        }

        Assert.Equal(0, state.FailedAccessAttemptCount);
        Assert.NotNull(state.LockoutEndUtc);
        Assert.True(state.IsLockedAt(UtcNow));
    }

    [Fact]
    public void Lockout_ExpiresAfterDefaultDuration()
    {
        var state = LockoutState.Create();

        for (var i = 0; i < LockoutState.DefaultMaxFailedAccessAttempts; i++)
        {
            state = state.RecordFailedAttempt(UtcNow);
        }

        var afterLockout = UtcNow.Add(LockoutState.DefaultLockoutDuration).AddMinutes(1);

        Assert.False(state.IsLockedAt(afterLockout));
    }

    [Fact]
    public void RecordFailedAttempt_WhileLocked_DoesNotChangeState()
    {
        var locked = LockoutState.Create()
            .RecordFailedAttempt(UtcNow);

        for (var i = 1; i < LockoutState.DefaultMaxFailedAccessAttempts; i++)
        {
            locked = locked.RecordFailedAttempt(UtcNow);
        }

        var lockedState = locked;

        var again = lockedState.RecordFailedAttempt(UtcNow);

        Assert.Equal(lockedState, again);
    }

    [Fact]
    public void Reset_ClearsAttemptsAndLockout()
    {
        var state = LockoutState.Create();

        for (var i = 0; i < LockoutState.DefaultMaxFailedAccessAttempts; i++)
        {
            state = state.RecordFailedAttempt(UtcNow);
        }

        Assert.True(state.IsLockedAt(UtcNow));

        state = state.Reset();

        Assert.Equal(0, state.FailedAccessAttemptCount);
        Assert.Null(state.LockoutEndUtc);
        Assert.False(state.IsLockedAt(UtcNow));
    }
}
