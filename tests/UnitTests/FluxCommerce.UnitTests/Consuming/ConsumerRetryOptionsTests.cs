using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 7.7: the retry policy and consumer configuration validation. <see cref="ConsumerRetryOptions.Validate"/>
/// and <see cref="RabbitMQConsumerOptions.Validate"/> fail fast on startup so misconfiguration can never
/// produce degenerate retry behavior (negative/undefined backoff, an attempt budget below 1) or route
/// terminal failures straight back into the processing loop (dead-letter topology aliasing the main
/// topology), which would be an endless retry.
/// </summary>
public class ConsumerRetryOptionsTests
{
    [Fact]
    public void Validate_DefaultOptions_IsValid()
    {
        var options = new ConsumerRetryOptions();

        options.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MaxAttemptsBelowOne_Throws(int maxAttempts)
    {
        var options = new ConsumerRetryOptions { MaxAttempts = maxAttempts };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MaxAttemptsOfOne_IsValid()
    {
        var options = new ConsumerRetryOptions { MaxAttempts = 1 };

        options.Validate();
    }

    [Fact]
    public void Validate_UndefinedBackoffType_Throws()
    {
        var options = new ConsumerRetryOptions { BackoffType = (ConsumerBackoffType)999 };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_NegativeInitialDelay_Throws()
    {
        var options = new ConsumerRetryOptions { InitialDelay = TimeSpan.FromMilliseconds(-1) };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ZeroInitialDelay_IsValid()
    {
        var options = new ConsumerRetryOptions { InitialDelay = TimeSpan.Zero };

        options.Validate();
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    public void Validate_DelayMultiplierBelowOne_Throws(double multiplier)
    {
        var options = new ConsumerRetryOptions { DelayMultiplier = multiplier };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_DelayMultiplierOfOne_IsValid()
    {
        var options = new ConsumerRetryOptions { DelayMultiplier = 1.0 };

        options.Validate();
    }

    [Fact]
    public void Validate_NegativeMaxDelay_Throws()
    {
        var options = new ConsumerRetryOptions { MaxDelay = TimeSpan.FromMilliseconds(-1) };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MaxDelayBelowInitialDelay_Throws()
    {
        var options = new ConsumerRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(5),
            MaxDelay = TimeSpan.FromSeconds(1)
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MaxDelayEqualToInitialDelay_IsValid()
    {
        var options = new ConsumerRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(5),
            MaxDelay = TimeSpan.FromSeconds(5)
        };

        options.Validate();
    }

    [Fact]
    public void Validate_NullNonRetryableExceptionTypes_Throws()
    {
        var options = new ConsumerRetryOptions { NonRetryableExceptionTypes = null! };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_DefaultConsumerOptions_IsValid()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events"
        };

        options.Validate();
        Assert.Equal("q.orders.dlx", options.ResolvedDeadLetterExchangeName);
        Assert.Equal("q.orders.dlq", options.ResolvedDeadLetterQueueName);
    }

    [Fact]
    public void Validate_ConsumerOptions_EmptyQueue_Throws()
    {
        var options = new RabbitMQConsumerOptions { ExchangeName = "fluxcommerce.events" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_EmptyExchange_Throws()
    {
        var options = new RabbitMQConsumerOptions { QueueName = "q.orders" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_ZeroPrefetch_Throws()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events",
            PrefetchCount = 0
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_ZeroConcurrency_Throws()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events",
            Concurrency = 0
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_DeadLetterExchangeAliasingMainExchange_Throws()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events",
            DeadLetterExchangeName = "fluxcommerce.events"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_DeadLetterQueueAliasingMainQueue_Throws()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events",
            DeadLetterQueueName = "q.orders"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ConsumerOptions_CustomDeadLetterTopology_IsValid()
    {
        var options = new RabbitMQConsumerOptions
        {
            QueueName = "q.orders",
            ExchangeName = "fluxcommerce.events",
            DeadLetterExchangeName = "fluxcommerce.events.dlx.custom",
            DeadLetterQueueName = "q.orders.dlq.custom"
        };

        options.Validate();
        Assert.Equal("fluxcommerce.events.dlx.custom", options.ResolvedDeadLetterExchangeName);
        Assert.Equal("q.orders.dlq.custom", options.ResolvedDeadLetterQueueName);
    }
}
