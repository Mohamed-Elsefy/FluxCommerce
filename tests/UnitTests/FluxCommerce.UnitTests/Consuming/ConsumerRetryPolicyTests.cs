using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 7.6: the consumer retry-policy math. The policy is bounded (exhaustion after MaxAttempts
/// total attempts) and deterministic (backoff follows the configured growth strategy and is capped at
/// MaxDelay). It is the CONSUMER processing retry — intentionally separate from the Phase 6 outbox
/// publication retry.
/// </summary>
public class ConsumerRetryPolicyTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    public void IsExhausted_ReachesMaxAttempts(int attempt, int maxAttempts, bool expected)
    {
        var options = new ConsumerRetryOptions { MaxAttempts = maxAttempts };

        Assert.Equal(expected, ConsumerRetryPolicy.IsExhausted(options, attempt));
    }

    [Theory]
    [InlineData(ConsumerBackoffType.Fixed, 1000, 2.0, 1)]
    [InlineData(ConsumerBackoffType.Fixed, 1000, 2.0, 2)]
    [InlineData(ConsumerBackoffType.Linear, 1000, 2.0, 1)]
    [InlineData(ConsumerBackoffType.Linear, 1000, 2.0, 3)]
    [InlineData(ConsumerBackoffType.Exponential, 1000, 2.0, 1)]
    [InlineData(ConsumerBackoffType.Exponential, 1000, 2.0, 3)]
    public void GetDelay_ComputesExpectedBackoff(
        ConsumerBackoffType type,
        double initialMilliseconds,
        double multiplier,
        int attempt)
    {
        var options = new ConsumerRetryOptions
        {
            BackoffType = type,
            InitialDelay = TimeSpan.FromMilliseconds(initialMilliseconds),
            DelayMultiplier = multiplier
        };

        var expectedMilliseconds = type switch
        {
            ConsumerBackoffType.Fixed => initialMilliseconds,
            ConsumerBackoffType.Linear => initialMilliseconds * attempt,
            ConsumerBackoffType.Exponential => initialMilliseconds * Math.Pow(multiplier, attempt - 1),
            _ => throw new ArgumentOutOfRangeException()
        };

        var delay = ConsumerRetryPolicy.GetDelay(options, attempt);

        Assert.InRange(delay.TotalMilliseconds, expectedMilliseconds - 0.01, expectedMilliseconds + 0.01);
    }

    [Fact]
    public void GetDelay_Exponential_IsCappedAtMaxDelay()
    {
        var options = new ConsumerRetryOptions
        {
            BackoffType = ConsumerBackoffType.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            DelayMultiplier = 2.0,
            MaxDelay = TimeSpan.FromSeconds(3)
        };

        var delay = ConsumerRetryPolicy.GetDelay(options, 5);

        Assert.Equal(TimeSpan.FromSeconds(3), delay);
    }

    [Fact]
    public void GetDelay_Fixed_NeverGrows()
    {
        var options = new ConsumerRetryOptions
        {
            BackoffType = ConsumerBackoffType.Fixed,
            InitialDelay = TimeSpan.FromMilliseconds(100)
        };

        Assert.Equal(TimeSpan.FromMilliseconds(100), ConsumerRetryPolicy.GetDelay(options, 1));
        Assert.Equal(TimeSpan.FromMilliseconds(100), ConsumerRetryPolicy.GetDelay(options, 2));
        Assert.Equal(TimeSpan.FromMilliseconds(100), ConsumerRetryPolicy.GetDelay(options, 3));
    }

    [Fact]
    public void GetDelay_AttemptZero_Throws()
    {
        var options = new ConsumerRetryOptions { InitialDelay = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() => ConsumerRetryPolicy.GetDelay(options, 0));
    }

    [Fact]
    public void GetDelay_NegativeOrZeroInitialDelay_ReturnsZeroDelay()
    {
        var options = new ConsumerRetryOptions
        {
            BackoffType = ConsumerBackoffType.Linear,
            InitialDelay = TimeSpan.Zero
        };

        Assert.Equal(TimeSpan.Zero, ConsumerRetryPolicy.GetDelay(options, 2));
    }
}
